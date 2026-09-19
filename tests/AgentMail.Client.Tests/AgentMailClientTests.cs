using System.Net;
using System.Text;
using AgentMail.Client;

namespace AgentMail.Client.Tests;

public sealed class AgentMailClientTests
{
    [Fact]
    public async Task CreateInboxAsync_UsesBearerAuthAndClientId()
    {
        var handler = new RecordingHandler("""{"inbox_id":"dotnet@agentmail.to","client_id":"customer-42"}""");
        using var httpClient = new HttpClient(handler);
        var client = new AgentMailClient(httpClient, "test-api-key");

        var inbox = await client.CreateInboxAsync(new CreateInboxRequest { ClientId = "customer-42" });

        Assert.Equal("dotnet@agentmail.to", inbox.InboxId);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v0/inboxes", handler.RequestUri?.AbsolutePath);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("test-api-key", handler.AuthorizationParameter);
        Assert.Contains(""client_id":"customer-42"", handler.Body);
    }

    [Fact]
    public async Task SendMessageAsync_SendsIdempotencyKey()
    {
        var handler = new RecordingHandler("""{"message_id":"msg_123","thread_id":"thr_123"}""");
        using var httpClient = new HttpClient(handler);
        var client = new AgentMailClient(httpClient, "test-api-key");

        var result = await client.SendMessageAsync(
            "dotnet@agentmail.to",
            new SendMessageRequest
            {
                To = ["person@example.com"],
                Subject = "Hello",
                Text = "World"
            },
            "order-4821-receipt");

        Assert.Equal("msg_123", result.MessageId);
        Assert.Equal("/v0/inboxes/dotnet%40agentmail.to/messages/send", handler.RequestUri?.AbsolutePath);
        Assert.Equal("order-4821-receipt", handler.IdempotencyKey);
    }

    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? IdempotencyKey { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            IdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var values)
                ? values.Single()
                : null;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
