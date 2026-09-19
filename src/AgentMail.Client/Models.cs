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

public sealed record Message
{
    [JsonPropertyName("inbox_id")] public required string InboxId { get; init; }
    [JsonPropertyName("thread_id")] public required string ThreadId { get; init; }
    [JsonPropertyName("message_id")] public required string MessageId { get; init; }
    [JsonPropertyName("labels")] public IReadOnlyList<string> Labels { get; init; } = [];
    [JsonPropertyName("timestamp")] public DateTimeOffset Timestamp { get; init; }
    [JsonPropertyName("from")] public required string From { get; init; }
    [JsonPropertyName("to")] public IReadOnlyList<string> To { get; init; } = [];
    [JsonPropertyName("cc")] public IReadOnlyList<string>? Cc { get; init; }
    [JsonPropertyName("bcc")] public IReadOnlyList<string>? Bcc { get; init; }
    [JsonPropertyName("reply_to")] public IReadOnlyList<string>? ReplyTo { get; init; }
    [JsonPropertyName("subject")] public string? Subject { get; init; }
    [JsonPropertyName("preview")] public string? Preview { get; init; }

    // Omitted by AgentMail when a webhook payload would exceed 1 MB.
    [JsonPropertyName("text")] public string? Text { get; init; }
    [JsonPropertyName("html")] public string? Html { get; init; }

    [JsonPropertyName("extracted_text")] public string? ExtractedText { get; init; }
    [JsonPropertyName("extracted_html")] public string? ExtractedHtml { get; init; }
    [JsonPropertyName("attachments")] public IReadOnlyList<Attachment>? Attachments { get; init; }
    [JsonPropertyName("in_reply_to")] public string? InReplyTo { get; init; }
    [JsonPropertyName("references")] public IReadOnlyList<string>? References { get; init; }
    [JsonPropertyName("headers")] public IReadOnlyDictionary<string, string>? Headers { get; init; }
    [JsonPropertyName("size")] public long Size { get; init; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}

public sealed record MessageThread
{
    [JsonPropertyName("inbox_id")] public required string InboxId { get; init; }
    [JsonPropertyName("thread_id")] public required string ThreadId { get; init; }
    [JsonPropertyName("labels")] public IReadOnlyList<string> Labels { get; init; } = [];
    [JsonPropertyName("timestamp")] public DateTimeOffset Timestamp { get; init; }
    [JsonPropertyName("senders")] public IReadOnlyList<string> Senders { get; init; } = [];
    [JsonPropertyName("recipients")] public IReadOnlyList<string> Recipients { get; init; } = [];
    [JsonPropertyName("subject")] public string? Subject { get; init; }
    [JsonPropertyName("preview")] public string? Preview { get; init; }
    [JsonPropertyName("last_message_id")] public string? LastMessageId { get; init; }
    [JsonPropertyName("message_count")] public int MessageCount { get; init; }
    [JsonPropertyName("attachments")] public IReadOnlyList<Attachment>? Attachments { get; init; }
    [JsonPropertyName("size")] public long Size { get; init; }
    [JsonPropertyName("received_timestamp")] public DateTimeOffset? ReceivedTimestamp { get; init; }
    [JsonPropertyName("sent_timestamp")] public DateTimeOffset? SentTimestamp { get; init; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}

public sealed record Attachment
{
    [JsonPropertyName("attachment_id")] public required string AttachmentId { get; init; }
    [JsonPropertyName("filename")] public string? Filename { get; init; }
    [JsonPropertyName("content_type")] public string? ContentType { get; init; }
    [JsonPropertyName("content_disposition")] public string? ContentDisposition { get; init; }
    [JsonPropertyName("content_id")] public string? ContentId { get; init; }
    [JsonPropertyName("size")] public long Size { get; init; }
}
