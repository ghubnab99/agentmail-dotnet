using System.Text.Json;
using System.Text.Json.Serialization;
using AgentMail.Client;

namespace AgentMail.AspNetCore;

public sealed record AgentMailWebhookEvent
{
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("event_type")] public required string EventType { get; init; }
    [JsonPropertyName("event_id")] public required string EventId { get; init; }

    // Populated for message.received.
    [JsonPropertyName("message")] public Message? Message { get; init; }
    [JsonPropertyName("thread")] public MessageThread? Thread { get; init; }

    // Payloads of other event types (send, delivery, bounce, ...) stay available here.
    [JsonExtensionData] public Dictionary<string, JsonElement>? Data { get; init; }
}

public static class AgentMailEventTypes
{
    public const string MessageReceived = "message.received";
    public const string MessageSent = "message.sent";
    public const string MessageDelivered = "message.delivered";
    public const string MessageBounced = "message.bounced";
    public const string MessageComplained = "message.complained";
    public const string MessageRejected = "message.rejected";
    public const string DomainVerified = "domain.verified";
}
