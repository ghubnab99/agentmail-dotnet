using AgentMail.Client;

var apiKey = Environment.GetEnvironmentVariable("AGENTMAIL_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Set AGENTMAIL_API_KEY before running the quickstart.");
    return 1;
}

using var httpClient = new HttpClient();
var client = new AgentMailClient(httpClient, apiKey);

var inbox = await client.CreateInboxAsync(new CreateInboxRequest
{
    ClientId = "agentmail-dotnet-quickstart-v1",
    DisplayName = "AgentMail .NET Quickstart"
});

Console.WriteLine($"Inbox ready: {inbox.InboxId}");

var recipient = Environment.GetEnvironmentVariable("AGENTMAIL_TO_EMAIL");
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
