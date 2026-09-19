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
}
