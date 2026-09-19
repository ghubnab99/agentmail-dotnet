using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentMail.AspNetCore.Tests;

public sealed class MapAgentMailWebhookTests : IAsyncDisposable
{
    private const string Route = "/webhooks/agentmail";
    private const string Secret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";

    private const string MessageReceivedJson = """
        {
          "type": "event",
          "event_type": "message.received",
          "event_id": "evt_123abc",
          "message": {
            "inbox_id": "support@agentmail.to",
            "thread_id": "thd_789ghi",
            "message_id": "<abc123@agentmail.to>",
            "labels": ["received"],
            "timestamp": "2026-09-19T10:00:00Z",
            "from": "Jane Doe <jane@example.com>",
            "to": ["Support Agent <support@agentmail.to>"],
            "subject": "Question about my account",
            "preview": "A short preview...",
            "text": "The full text body of the email.",
            "attachments": [
              { "attachment_id": "att_1", "filename": "statement.pdf", "content_type": "application/pdf", "size": 1234 }
            ],
            "size": 2048,
            "updated_at": "2026-09-19T10:00:00Z",
            "created_at": "2026-09-19T10:00:00Z"
          },
          "thread": {
            "inbox_id": "support@agentmail.to",
            "thread_id": "thd_789ghi",
            "labels": ["received"],
            "timestamp": "2026-09-19T10:00:00Z",
            "senders": ["Jane Doe <jane@example.com>"],
            "recipients": ["Support Agent <support@agentmail.to>"],
            "last_message_id": "<abc123@agentmail.to>",
            "message_count": 3,
            "size": 2048,
            "updated_at": "2026-09-19T10:00:00Z",
            "created_at": "2026-09-19T10:00:00Z"
          }
        }
        """;

    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 9, 19, 10, 0, 0, TimeSpan.Zero));
    private readonly List<AgentMailWebhookEvent> _handled = [];
    private WebApplication? _app;

    private async Task<HttpClient> StartAsync(
        Func<AgentMailWebhookEvent, IServiceProvider, CancellationToken, Task>? handler = null,
        bool registerWebhooks = true,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<TimeProvider>(_time);
        if (registerWebhooks)
        {
            builder.Services.AddAgentMailWebhooks(o => o.Secret = Secret);
        }

        configureServices?.Invoke(builder.Services);

        _app = builder.Build();
        _app.MapAgentMailWebhook(Route, handler ?? ((evt, _, _) =>
        {
            _handled.Add(evt);
            return Task.CompletedTask;
        }));

        await _app.StartAsync();
        return _app.GetTestClient();
    }

    private HttpRequestMessage SignedRequest(
        string body,
        string deliveryId = "msg_delivery_1",
        string headerPrefix = "svix",
        string? signature = null)
    {
        var timestamp = _time.GetUtcNow().ToUnixTimeSeconds().ToString();
        var request = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add($"{headerPrefix}-id", deliveryId);
        request.Headers.Add($"{headerPrefix}-timestamp", timestamp);
        request.Headers.Add($"{headerPrefix}-signature", signature ?? Svix.Sign(Secret, deliveryId, timestamp, body));
        return request;
    }

    [Fact]
    public async Task ValidDelivery_IsDeserializedIntoTypedEvent()
    {
        var client = await StartAsync();

        using var response = await client.SendAsync(SignedRequest(MessageReceivedJson));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var evt = Assert.Single(_handled);
        Assert.Equal(AgentMailEventTypes.MessageReceived, evt.EventType);
        Assert.Equal("evt_123abc", evt.EventId);
        Assert.Equal("Jane Doe <jane@example.com>", evt.Message?.From);
        Assert.Equal("Question about my account", evt.Message?.Subject);
        Assert.Equal("statement.pdf", Assert.Single(evt.Message!.Attachments!).Filename);
        Assert.Equal(3, evt.Thread?.MessageCount);
    }

    [Fact]
    public async Task InvalidSignature_Returns400WithoutInvokingHandler()
    {
        var client = await StartAsync();

        using var response = await client.SendAsync(
            SignedRequest(MessageReceivedJson, signature: Svix.Signature));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_handled);
    }

    [Fact]
    public async Task BodyModifiedAfterSigning_Returns400()
    {
        var client = await StartAsync();
        var request = SignedRequest(MessageReceivedJson);
        request.Content = new StringContent(
            MessageReceivedJson.Replace("Jane Doe", "Mallory"), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_handled);
    }

    [Fact]
    public async Task ReplayedOldDelivery_Returns400()
    {
        var client = await StartAsync();
        var request = SignedRequest(MessageReceivedJson);
        _time.Now += TimeSpan.FromMinutes(10);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SignedButMalformedPayload_Returns400()
    {
        var client = await StartAsync();

        using var response = await client.SendAsync(SignedRequest("""{"unexpected": true}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_handled);
    }

    [Fact]
    public async Task DuplicateDelivery_IsAcknowledgedButHandledOnce()
    {
        var client = await StartAsync();

        using var first = await client.SendAsync(SignedRequest(MessageReceivedJson));
        using var retry = await client.SendAsync(SignedRequest(MessageReceivedJson));
        using var other = await client.SendAsync(SignedRequest(MessageReceivedJson, deliveryId: "msg_delivery_2"));

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, other.StatusCode);
        Assert.Equal(2, _handled.Count);
    }

    [Fact]
    public async Task FailedHandler_AllowsRetryToBeProcessed()
    {
        var attempts = 0;
        var client = await StartAsync((_, _, _) =>
            ++attempts == 1 ? throw new InvalidOperationException("downstream unavailable") : Task.CompletedTask);

        HttpStatusCode? firstStatus = null;
        try
        {
            using var first = await client.SendAsync(SignedRequest(MessageReceivedJson));
            firstStatus = first.StatusCode;
        }
        catch (InvalidOperationException)
        {
            // TestServer surfaces unhandled exceptions to the caller; a real server returns 500.
        }

        using var retry = await client.SendAsync(SignedRequest(MessageReceivedJson));

        Assert.NotEqual(HttpStatusCode.NoContent, firstStatus);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task UnbrandedWebhookHeaders_AreAccepted()
    {
        var client = await StartAsync();

        using var response = await client.SendAsync(SignedRequest(MessageReceivedJson, headerPrefix: "webhook"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(_handled);
    }

    [Fact]
    public async Task Handler_CanResolveApplicationServices()
    {
        var sink = new List<string>();
        var client = await StartAsync(
            (evt, services, _) =>
            {
                services.GetRequiredService<List<string>>().Add(evt.EventId);
                return Task.CompletedTask;
            },
            configureServices: services => services.AddSingleton(sink));

        using var response = await client.SendAsync(SignedRequest(MessageReceivedJson));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["evt_123abc"], sink);
    }

    [Fact]
    public async Task WithoutAddAgentMailWebhooks_Returns500()
    {
        var client = await StartAsync(registerWebhooks: false);

        using var response = await client.SendAsync(SignedRequest(MessageReceivedJson));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(_handled);
    }

    [Fact]
    public async Task MissingSecret_FailsAtStartup()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAgentMailWebhooks(_ => { });
        _app = builder.Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => _app.StartAsync());
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
