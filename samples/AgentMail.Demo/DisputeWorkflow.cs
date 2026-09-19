using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentMail.AspNetCore;
using AgentMail.Client;

namespace AgentMail.Demo;

public sealed class DisputeWorkflow(DemoState state, ICaseExtractor extractor, IAgentMailClient agentMail, ILogger<DisputeWorkflow> logger)
{
    // Runs only for deliveries that passed signature verification, parsed into a typed event
    // and were claimed as a first delivery by the deduplicator.
    public void Handle(AgentMailWebhookEvent evt, string deliveryId, CapturedDelivery? rawDelivery)
    {
        switch (evt)
        {
            case { EventType: AgentMailEventTypes.MessageReceived, Message: { } message }:
                OnMessageReceived(message, deliveryId, rawDelivery);
                break;
            case { EventType: AgentMailEventTypes.MessageSent or AgentMailEventTypes.MessageDelivered }:
                OnLifecycle(evt, "success");
                break;
            case { EventType: AgentMailEventTypes.MessageBounced or AgentMailEventTypes.MessageRejected or AgentMailEventTypes.MessageComplained }:
                OnLifecycle(evt, "danger");
                break;
            default:
                state.Log("info", $"AgentMail {evt.EventType}", "Verified; no workflow step for this event type");
                break;
        }
    }

    public async Task ApproveAsync(string caseId, CancellationToken cancellationToken)
    {
        var disputeCase = state.FindById(caseId) ?? throw new KeyNotFoundException(caseId);

        var proceed = false;
        state.Update(() =>
        {
            proceed = disputeCase.RecipientAllowed &&
                      disputeCase.Status is CaseStatus.AwaitingApproval or CaseStatus.ReplyFailed;
            if (proceed)
            {
                disputeCase.Status = CaseStatus.Sending;
                disputeCase.Error = null;
            }
        });

        if (!proceed)
        {
            return;
        }

        state.Log("success", "Human approved response", $"Reply to {disputeCase.CustomerAddress}", caseId);
        state.Log("info", "Reply requested", $"Idempotency-Key: {disputeCase.IdempotencyKey}", caseId);

        try
        {
            var result = await agentMail.ReplyMessageAsync(
                disputeCase.InboxId,
                disputeCase.InboundMessageId,
                new ReplyMessageRequest { Text = disputeCase.DraftReply },
                disputeCase.IdempotencyKey,
                cancellationToken);

            state.Update(() =>
            {
                disputeCase.ReplyMessageId = result.MessageId;
                if (disputeCase.Status == CaseStatus.Sending)
                {
                    disputeCase.Status = CaseStatus.ReplySent;
                }
            });
            state.Log("success", "Reply accepted by AgentMail", "Threaded under the customer's original email", caseId);
        }
        catch (AgentMailApiException ex)
        {
            logger.LogWarning("AgentMail rejected the reply for {CaseId}: HTTP {Status}", caseId, (int?)ex.StatusCode);
            state.Update(() =>
            {
                disputeCase.Status = CaseStatus.ReplyFailed;
                disputeCase.Error = $"AgentMail returned HTTP {(int?)ex.StatusCode}";
            });
            state.Log("danger", "Reply failed", $"AgentMail returned HTTP {(int?)ex.StatusCode}", caseId);
        }
    }

    private void OnMessageReceived(Message message, string deliveryId, CapturedDelivery? rawDelivery)
    {
        var existing = state.FindByThread(message.ThreadId);
        if (existing is not null)
        {
            state.LinkDelivery(deliveryId, existing.Id);
            state.Update(() => existing.FollowUps++);
            state.Log("info", "Customer follow-up received", $"Added to existing case {existing.Id}; no new case", existing.Id);
            return;
        }

        var (name, address) = ParseSender(message.From);
        var body = message.ExtractedText ?? message.Text ?? message.Preview;
        var details = extractor.Extract(message.Subject, body);
        var allowed = state.AllowedRecipients.Contains(address, StringComparer.OrdinalIgnoreCase);
        var caseId = state.NextCaseId();

        var disputeCase = new DisputeCase
        {
            Id = caseId,
            InboxId = message.InboxId,
            ThreadId = message.ThreadId,
            InboundMessageId = message.MessageId,
            CustomerName = name,
            CustomerAddress = address,
            Subject = message.Subject,
            Details = details,
            ExtractionMethod = extractor.Method,
            OpenedAt = state.Now,
            DraftReply = DraftReply(caseId, name, details),
            RecipientAllowed = allowed,
            // One acknowledgement per inbound email, stable across restarts. A retry of this send can
            // never produce a second email; a different logical response would get a new version suffix.
            IdempotencyKey = $"dispute:{Hash(message.MessageId)}:acknowledgement:v1",
            Status = allowed ? CaseStatus.AwaitingApproval : CaseStatus.ReplyBlocked
        };

        state.AddCase(disputeCase, deliveryId);
        if (rawDelivery is not null)
        {
            state.Capture(rawDelivery with { CaseId = caseId });
        }

        state.Log("success", "Customer email received", $"{message.From} · \"{message.Subject}\"", caseId);
        state.Log("success", "Webhook verified", "Svix signature · typed .NET event · first delivery", caseId);
        state.Log("success", $"Case {caseId} opened", Describe(details), caseId);
        state.Log(allowed ? "info" : "danger",
            allowed ? "Awaiting human approval" : "Reply blocked by recipient policy",
            allowed ? "Draft acknowledgement prepared" : $"{address} is not on the allowlist",
            caseId);
    }

    private void OnLifecycle(AgentMailWebhookEvent evt, string kind)
    {
        // Lifecycle events carry one payload object ("send", "delivery", "bounce", ...) holding a thread_id.
        var payload = evt.Data?.Values
            .Where(v => v.ValueKind == JsonValueKind.Object && v.TryGetProperty("thread_id", out _))
            .Select(v => (JsonElement?)v)
            .FirstOrDefault();

        string? threadId = null;
        string? recipients = null;
        if (payload is { } p)
        {
            threadId = p.GetProperty("thread_id").GetString();
            if (p.TryGetProperty("recipients", out var r) && r.ValueKind == JsonValueKind.Array)
            {
                recipients = string.Join(", ", r.EnumerateArray()
                    .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()
                        : x.ValueKind == JsonValueKind.Object && x.TryGetProperty("address", out var a) ? a.GetString()
                        : null)
                    .Where(x => x is not null));
            }
        }

        var disputeCase = state.FindByThread(threadId);
        if (disputeCase is not null && evt.EventType == AgentMailEventTypes.MessageDelivered)
        {
            state.Update(() =>
            {
                if (disputeCase.Status is CaseStatus.Sending or CaseStatus.ReplySent)
                {
                    disputeCase.Status = CaseStatus.ReplyDelivered;
                }
            });
        }

        state.Log(kind, $"AgentMail {evt.EventType}", string.IsNullOrEmpty(recipients) ? null : $"To {recipients}", disputeCase?.Id);
    }

    private static (string Name, string Address) ParseSender(string from)
    {
        if (MailAddress.TryCreate(from, out var address))
        {
            return (string.IsNullOrWhiteSpace(address.DisplayName) ? address.User : address.DisplayName, address.Address);
        }

        return (from, from);
    }

    private static string DraftReply(string caseId, string customerName, DisputeDetails details)
    {
        var greeting = customerName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
        var what = details.Issue.ToLowerInvariant();
        var amount = details.Amount is null ? "" : $" of {details.Amount}";
        var merchant = details.Merchant is null ? "" : $" at {details.Merchant}";
        var date = details.TransactionDate is null ? "" : $" on {details.TransactionDate}";

        return $"""
            Hi {greeting},

            Thank you for contacting us. We've received your report of a {what}{amount}{merchant}{date}.

            Your case reference is {caseId}. Our team will review the transaction and update you within 2 business days. Please quote this reference in any follow-up.

            Customer Operations
            """;
    }

    private static string Describe(DisputeDetails d) =>
        string.Join(" · ", new[] { d.Issue, d.Amount, d.Merchant, d.TransactionDate }.Where(x => x is not null));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();
}
