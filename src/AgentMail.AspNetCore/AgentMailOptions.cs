namespace AgentMail.AspNetCore;

public sealed class AgentMailOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public Uri BaseAddress { get; set; } = AgentMail.Client.AgentMailClient.DefaultBaseAddress;
}
