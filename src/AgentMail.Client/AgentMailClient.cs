using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentMail.Client;

public sealed class AgentMailClient : IAgentMailClient
{
    public static readonly Uri DefaultBaseAddress = new("https://api.agentmail.to/");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public AgentMailClient(HttpClient httpClient, string apiKey, Uri? baseAddress = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        _httpClient = httpClient;
        _httpClient.BaseAddress ??= baseAddress ?? DefaultBaseAddress;
        _httpClient.DefaultRequestHeaders.Authorization ??=
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<Inbox> CreateInboxAsync(
        CreateInboxRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await _httpClient.PostAsJsonAsync(
            "v0/inboxes", request, JsonOptions, cancellationToken).ConfigureAwait(false);

        return await ReadAsync<Inbox>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Inbox> GetInboxAsync(
        string inboxId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inboxId);

        using var response = await _httpClient.GetAsync(
            $"v0/inboxes/{Uri.EscapeDataString(inboxId)}",
            cancellationToken).ConfigureAwait(false);

        return await ReadAsync<Inbox>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SendMessageResponse> SendMessageAsync(
        string inboxId,
        SendMessageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inboxId);
        ArgumentNullException.ThrowIfNull(request);

        return await PostSendAsync(
            $"v0/inboxes/{Uri.EscapeDataString(inboxId)}/messages/send",
            request, idempotencyKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SendMessageResponse> ReplyMessageAsync(
        string inboxId,
        string messageId,
        ReplyMessageRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inboxId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentNullException.ThrowIfNull(request);

        return await PostSendAsync(
            $"v0/inboxes/{Uri.EscapeDataString(inboxId)}/messages/{Uri.EscapeDataString(messageId)}/reply",
            request, idempotencyKey, cancellationToken).ConfigureAwait(false);
    }

    // AgentMail makes sends, replies and forwards idempotent through the Idempotency-Key header:
    // a retry with the same key returns the original message instead of sending again.
    private async Task<SendMessageResponse> PostSendAsync<TRequest>(
        string path,
        TRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            message.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<SendMessageResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> ReadAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = response.Content is null
                ? null
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            throw new AgentMailApiException(response.StatusCode, body);
        }

        var value = await response.Content.ReadFromJsonAsync<T>(
            JsonOptions, cancellationToken).ConfigureAwait(false);

        return value ?? throw new InvalidDataException("AgentMail returned an invalid JSON response.");
    }
}
