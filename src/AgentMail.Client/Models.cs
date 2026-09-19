using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentMail.Client;

public sealed record CreateInboxRequest
{
    [JsonPropertyName("username")] public string? Username { get; init; }
    [JsonPropertyName("domain")] public string? Domain { get; init; }
    [JsonPropertyName("display_name")] public string? DisplayName { get; init; }
    [JsonPropertyName("client_id")] public string? ClientId { get; init; }
}

public sealed record Inbox
{
    [JsonPropertyName("inbox_id")] public required string InboxId { get; init; }
    [JsonPropertyName("username")] public string? Username { get; init; }
    [JsonPropertyName("domain")] public string? Domain { get; init; }
    [JsonPropertyName("display_name")] public string? DisplayName { get; init; }
    [JsonPropertyName("client_id")] public string? ClientId { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}

public sealed record SendMessageRequest
{
    [JsonPropertyName("to")] public required IReadOnlyList<string> To { get; init; }
    [JsonPropertyName("subject")] public required string Subject { get; init; }
    [JsonPropertyName("text")] public required string Text { get; init; }
    [JsonPropertyName("html")] public string? Html { get; init; }
    [JsonPropertyName("cc")] public IReadOnlyList<string>? Cc { get; init; }
    [JsonPropertyName("bcc")] public IReadOnlyList<string>? Bcc { get; init; }
    [JsonPropertyName("reply_to")] public IReadOnlyList<string>? ReplyTo { get; init; }
}

public sealed record SendMessageResponse
{
    [JsonPropertyName("message_id")] public string? MessageId { get; init; }
    [JsonPropertyName("thread_id")] public string? ThreadId { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}
