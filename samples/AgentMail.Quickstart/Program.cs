using AgentMail.Client;
using Microsoft.Extensions.Configuration;

// Store once with: dotnet user-secrets set "AgentMail:ApiKey" "am_..." --project samples/AgentMail.Quickstart
// AGENTMAIL_API_KEY / AGENTMAIL_TO_EMAIL environment variables are still honored.
var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var apiKey = Setting("AgentMail:ApiKey", "AGENTMAIL_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("""
        No AgentMail API key found. Store it once with:
          dotnet user-secrets set "AgentMail:ApiKey" "am_..." --project samples/AgentMail.Quickstart
        or set the AGENTMAIL_API_KEY environment variable.
        """);
    return 1;
}

using var httpClient = new HttpClient();
var client = new AgentMailClient(httpClient, apiKey);

Inbox inbox;
try
{
    inbox = await client.CreateInboxAsync(new CreateInboxRequest
    {
        ClientId = "agentmail-dotnet-quickstart-v1",
        DisplayName = "AgentMail .NET Quickstart"
    });
}
catch (AgentMailApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
{
    Console.Error.WriteLine($"AgentMail rejected the API key (HTTP {(int)ex.StatusCode}). Check that it is current and not revoked.");
    return 1;
}

Console.WriteLine($"Inbox ready: {inbox.InboxId}");

var recipient = Setting("AgentMail:ToEmail", "AGENTMAIL_TO_EMAIL");
if (!string.IsNullOrWhiteSpace(recipient))
{
    var result = await client.SendMessageAsync(
        inbox.InboxId,
        new SendMessageRequest
        {
            To = [recipient],
            Subject = "Hello from AgentMail .NET",
            Text = "Sent through the community AgentMail .NET client."
        },
        idempotencyKey: $"quickstart-{DateTimeOffset.UtcNow:yyyyMMddHH}");

    Console.WriteLine($"Message sent: {result.MessageId ?? "(no message_id returned)"}");
}

return 0;

string? Setting(string key, string environmentVariable) =>
    configuration[key] is { Length: > 0 } value ? value : configuration[environmentVariable];
