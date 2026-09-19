namespace AgentMail.AspNetCore;

public sealed class AgentMailWebhookOptions
{
    // The endpoint signing secret from the AgentMail console, e.g. "whsec_...".
    public string Secret { get; set; } = string.Empty;
    public TimeSpan TimestampTolerance { get; set; } = AgentMailWebhookVerifier.DefaultTolerance;

    // How long a delivered svix-id is remembered. Should exceed the sender's retry schedule.
    public TimeSpan DeduplicationWindow { get; set; } = TimeSpan.FromHours(24);
}
