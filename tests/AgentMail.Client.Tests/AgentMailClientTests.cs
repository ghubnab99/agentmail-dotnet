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
        Assert.Contains("\"client_id\":\"customer-42\"", handler.Body);
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

    [Fact]
    public async Task ReplyMessageAsync_PostsToEscapedReplyPathWithIdempotencyKey()
    {
        var handler = new RecordingHandler("""{"message_id":"<reply@agentmail.to>","thread_id":"thd_1"}""");
        using var httpClient = new HttpClient(handler);
        var client = new AgentMailClient(httpClient, "test-api-key");

        var result = await client.ReplyMessageAsync(
            "support@agentmail.to",
            "<abc123@agentmail.to>",
            new ReplyMessageRequest { Text = "Thanks, we are on it." },
            "dispute.abc.acknowledgement.v1");

        Assert.Equal("<reply@agentmail.to>", result.MessageId);
        Assert.Equal("thd_1", result.ThreadId);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(
            "/v0/inboxes/support%40agentmail.to/messages/%3Cabc123%40agentmail.to%3E/reply",
            handler.RequestUri?.AbsolutePath);
        Assert.Equal("dispute.abc.acknowledgement.v1", handler.IdempotencyKey);
        Assert.Equal("""{"text":"Thanks, we are on it."}""", handler.Body);
    }

    [Theory]
    [InlineData("dispute:abc:v1")]
    [InlineData("case/4821")]
    [InlineData("order 4821")]
    public async Task SendMessageAsync_RejectsIdempotencyKeysAgentMailWouldRefuse(string idempotencyKey)
    {
        // AgentMail allows only A-Z a-z 0-9 - . _ ~ and answers anything else with HTTP 400.
        var handler = new RecordingHandler("""{"message_id":"m","thread_id":"t"}""");
        using var httpClient = new HttpClient(handler);
        var client = new AgentMailClient(httpClient, "test-api-key");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => client.SendMessageAsync(
            "support@agentmail.to",
            new SendMessageRequest { To = ["customer@example.com"], Subject = "Hi", Text = "Hi" },
            idempotencyKey));

        Assert.Equal("idempotencyKey", exception.ParamName);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task ReplyMessageAsync_SerializesOptionalFieldsInAgentMailShape()
    {
        var handler = new RecordingHandler("""{"message_id":"m","thread_id":"t"}""");
        using var httpClient = new HttpClient(handler);
        var client = new AgentMailClient(httpClient, "test-api-key");

        await client.ReplyMessageAsync("inbox", "msg", new ReplyMessageRequest
        {
            Html = "<p>Hi</p>",
            ReplyAll = true,
            Cc = ["ops@example.com"],
            Labels = ["dispute"]
        });

        Assert.Contains("\"html\":\"\\u003Cp\\u003EHi\\u003C/p\\u003E\"", handler.Body);
        Assert.Contains("\"reply_all\":true", handler.Body);
        Assert.Contains("\"cc\":[\"ops@example.com\"]", handler.Body);
        Assert.Contains("\"labels\":[\"dispute\"]", handler.Body);
        Assert.DoesNotContain("\"to\"", handler.Body);
        Assert.Null(handler.IdempotencyKey);
    }

    [Fact]
    public async Task ReplyMessageAsync_ReusedKeyConflictSurfacesAsApiException()
    {
        var handler = new RecordingHandler("""{"name":"ConflictError"}""", HttpStatusCode.Conflict);
        using var httpClient = new HttpClient(handler);
        var client = new AgentMailClient(httpClient, "test-api-key");

        var ex = await Assert.ThrowsAsync<AgentMailApiException>(() => client.ReplyMessageAsync(
            "inbox", "msg", new ReplyMessageRequest { Text = "different content" }, "reused-key"));

        Assert.Equal(HttpStatusCode.Conflict, ex.StatusCode);
        Assert.Contains("ConflictError", ex.ResponseBody);
    }

    [Theory]
    [InlineData("", "msg")]
    [InlineData("inbox", " ")]
    public async Task ReplyMessageAsync_RequiresInboxAndMessageIds(string inboxId, string messageId)
    {
        using var httpClient = new HttpClient(new RecordingHandler("{}"));
        var client = new AgentMailClient(httpClient, "test-api-key");

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            client.ReplyMessageAsync(inboxId, messageId, new ReplyMessageRequest { Text = "x" }));
    }

    private sealed class RecordingHandler(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
        : HttpMessageHandler
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

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
