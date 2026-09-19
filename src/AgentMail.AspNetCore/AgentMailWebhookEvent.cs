using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentMail.AspNetCore;

public sealed record AgentMailWebhookEvent
{
    [JsonPropertyName("event_type")] public required string EventType { get; init; }
    [JsonPropertyName("event_id")] public required string EventId { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Data { get; init; }
}
