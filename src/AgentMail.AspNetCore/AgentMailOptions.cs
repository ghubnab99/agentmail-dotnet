namespace AgentMail.AspNetCore;

public sealed class AgentMailOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public Uri BaseAddress { get; set; } = AgentMail.Client.AgentMailClient.DefaultBaseAddress;
    public string? WebhookSecret { get; set; }
    public TimeSpan WebhookTimestampTolerance { get; set; } = TimeSpan.FromMinutes(5);
}
