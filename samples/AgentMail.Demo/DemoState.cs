using System.Threading.Channels;

namespace AgentMail.Demo;

public enum CaseStatus
{
    AwaitingApproval,
    Sending,
    ReplySent,
    ReplyDelivered,
    ReplyBlocked,
    ReplyFailed
}

public sealed record TimelineEntry(DateTimeOffset At, string Kind, string Title, string? Detail, string? CaseId);

public sealed class DisputeCase
{
    public required string Id { get; init; }
    public required string InboxId { get; init; }
    public required string ThreadId { get; init; }
    public required string InboundMessageId { get; init; }
    public required string CustomerName { get; init; }
    public required string CustomerAddress { get; init; }
    public required string? Subject { get; init; }
    public required DisputeDetails Details { get; init; }
    public required string ExtractionMethod { get; init; }
    public required DateTimeOffset OpenedAt { get; init; }
    public required string DraftReply { get; init; }
    public required bool RecipientAllowed { get; init; }
    public required string IdempotencyKey { get; init; }
    public CaseStatus Status { get; set; }
    public string? ReplyMessageId { get; set; }
    public string? Error { get; set; }
    public int DuplicatesBlocked { get; set; }
    public int FollowUps { get; set; }
}

// Exact bytes and signature headers of the last authentic delivery; memory only, never logged or persisted.
public sealed record CapturedDelivery(
    byte[] Body, string Id, string Timestamp, string Signature, string CaseId, DateTimeOffset CapturedAt);

public sealed class DemoState(TimeProvider time)
{
    private readonly object _gate = new();
    private readonly List<TimelineEntry> _timeline = [];
    private readonly List<DisputeCase> _cases = [];
    private readonly Dictionary<string, string> _caseByDelivery = new(StringComparer.Ordinal);
    private readonly List<Channel<bool>> _subscribers = [];
    private int _nextCaseNumber = 1001;

    public string? InboxId { get; set; }
    public string? InboxDisplayName { get; set; }
    public IReadOnlyList<string> AllowedRecipients { get; set; } = [];
    public CapturedDelivery? Captured { get; private set; }

    public DateTimeOffset Now => time.GetUtcNow();

    public string NextCaseId()
    {
        lock (_gate)
        {
            return $"DSP-{_nextCaseNumber++}";
        }
    }

    public void AddCase(DisputeCase disputeCase, string deliveryId)
    {
        lock (_gate)
        {
            _cases.Add(disputeCase);
            _caseByDelivery[deliveryId] = disputeCase.Id;
        }
    }

    public DisputeCase? FindByThread(string? threadId)
    {
        lock (_gate)
        {
            return _cases.LastOrDefault(c => c.ThreadId == threadId);
        }
    }

    public DisputeCase? FindById(string caseId)
    {
        lock (_gate)
        {
            return _cases.FirstOrDefault(c => c.Id == caseId);
        }
    }

    public DisputeCase? FindByDelivery(string deliveryId)
    {
        lock (_gate)
        {
            return _caseByDelivery.TryGetValue(deliveryId, out var id) ? _cases.FirstOrDefault(c => c.Id == id) : null;
        }
    }

    public void LinkDelivery(string deliveryId, string caseId)
    {
        lock (_gate)
        {
            _caseByDelivery[deliveryId] = caseId;
        }
    }

    public void Capture(CapturedDelivery delivery)
    {
        lock (_gate)
        {
            Captured = delivery;
        }

        Notify();
    }

    public void Update(Action change)
    {
        lock (_gate)
        {
            change();
        }

        Notify();
    }

    public void Log(string kind, string title, string? detail = null, string? caseId = null)
    {
        lock (_gate)
        {
            _timeline.Add(new TimelineEntry(Now, kind, title, detail, caseId));
        }

        Notify();
    }

    public object Snapshot()
    {
        lock (_gate)
        {
            return new
            {
                inbox = InboxId,
                inboxDisplayName = InboxDisplayName,
                allowedRecipients = AllowedRecipients,
                cases = _cases.Select(c => new
                {
                    c.Id,
                    c.CustomerName,
                    c.CustomerAddress,
                    c.Subject,
                    c.Details,
                    c.ExtractionMethod,
                    c.OpenedAt,
                    c.DraftReply,
                    c.RecipientAllowed,
                    c.IdempotencyKey,
                    Status = c.Status.ToString(),
                    c.ReplyMessageId,
                    c.Error,
                    c.DuplicatesBlocked,
                    c.FollowUps
                }).ToList(),
                timeline = _timeline.ToList(),
                replay = Captured is null ? null : new
                {
                    Captured.CaseId,
                    Captured.CapturedAt,
                    SignedAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(Captured.Timestamp))
                }
            };
        }
    }

    public ChannelReader<bool> Subscribe(out Action unsubscribe)
    {
        var channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        lock (_gate)
        {
            _subscribers.Add(channel);
        }

        unsubscribe = () =>
        {
            lock (_gate)
            {
                _subscribers.Remove(channel);
            }
        };
        return channel.Reader;
    }

    private void Notify()
    {
        lock (_gate)
        {
            foreach (var subscriber in _subscribers)
            {
                subscriber.Writer.TryWrite(true);
            }
        }
    }
}
