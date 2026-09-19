namespace AgentMail.Client;

public interface IAgentMailClient
{
    Task<Inbox> CreateInboxAsync(CreateInboxRequest request, CancellationToken cancellationToken = default);
    Task<Inbox> GetInboxAsync(string inboxId, CancellationToken cancellationToken = default);
    Task<SendMessageResponse> SendMessageAsync(
        string inboxId,
        SendMessageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>Replies to a message in its existing thread.</summary>
    Task<SendMessageResponse> ReplyMessageAsync(
        string inboxId,
        string messageId,
        ReplyMessageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);
}
