using AgentMail.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Store the secret with: dotnet user-secrets set "AgentMail:WebhookSecret" "whsec_..."
builder.Services.AddAgentMailWebhooks(options =>
    options.Secret = builder.Configuration["AgentMail:WebhookSecret"] ?? string.Empty);

var app = builder.Build();

app.MapGet("/", () => "AgentMail webhook sample is running. POST events to /webhooks/agentmail.");

app.MapAgentMailWebhook("/webhooks/agentmail", (evt, services, _) =>
{
    var logger = services.GetRequiredService<ILogger<Program>>();

    if (evt is { EventType: AgentMailEventTypes.MessageReceived, Message: { } message })
    {
        // Bodies are deliberately not logged; email content is untrusted and may contain PII.
        logger.LogInformation(
            "Verified {EventType} {EventId}: from {From} to {Inbox}, subject \"{Subject}\", thread {ThreadId} ({MessageCount} messages), {AttachmentCount} attachments",
            evt.EventType,
            evt.EventId,
            message.From,
            message.InboxId,
            message.Subject,
            message.ThreadId,
            evt.Thread?.MessageCount,
            message.Attachments?.Count ?? 0);
    }
    else
    {
        logger.LogInformation("Verified {EventType} {EventId}", evt.EventType, evt.EventId);
    }

    // Real handlers should enqueue work and return quickly so AgentMail does not time out and retry.
    return Task.CompletedTask;
});

app.Run();
