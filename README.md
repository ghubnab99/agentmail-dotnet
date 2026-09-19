# AgentMail .NET

Production-oriented community .NET SDK and ASP.NET Core integration for [AgentMail](https://agentmail.to).

> **Status:** early preview. This is an independent community project and is not an official AgentMail SDK.

## Why this exists

AgentMail gives AI agents programmable email inboxes. This project brings that API into the .NET ecosystem with patterns expected in production services: typed clients, cancellation, idempotency, dependency injection, verified webhooks, and a path toward observability and enterprise hardening.

## Current vertical slice

- Create an inbox with `client_id` idempotency
- Retrieve an inbox
- Send email with optional `Idempotency-Key`
- Bearer authentication
- ASP.NET Core dependency injection
- Svix webhook signature verification (tested against Svix's reference vectors)
- Timestamp tolerance for replay resistance
- Duplicate-delivery protection keyed on `svix-id`, pluggable for shared stores
- Typed `message.received` events (message, thread, attachments)
- Webhook-only hosting without an API key
- CI build, tests, and preview NuGet packing

## Quickstart

Set `AGENTMAIL_API_KEY`, then run:

```bash
dotnet run --project samples/AgentMail.Quickstart
```

Set `AGENTMAIL_TO_EMAIL` as well if you want the sample to send a message after creating/reusing its inbox.

## ASP.NET Core

Sending:

```csharp
builder.Services.AddAgentMail(options =>
    options.ApiKey = builder.Configuration["AgentMail:ApiKey"]!);
```

Receiving webhooks:

```csharp
builder.Services.AddAgentMailWebhooks(options =>
    options.Secret = builder.Configuration["AgentMail:WebhookSecret"]!);

app.MapAgentMailWebhook("/webhooks/agentmail", async (evt, services, ct) =>
{
    if (evt is { EventType: AgentMailEventTypes.MessageReceived, Message: { } message })
    {
        // message.From, message.Subject, message.Text, evt.Thread, ...
        // Enqueue work and return quickly.
    }
});
```

Each delivery is processed in this order:

1. The Svix signature is verified over the raw body with AgentMail's `svix-id`, `svix-timestamp` and `svix-signature` headers. Invalid or stale requests get `400`.
2. The payload is deserialized into `AgentMailWebhookEvent`.
3. The `svix-id` is claimed. AgentMail reuses it for retries, so a repeated delivery is acknowledged with `204` without running the handler again.
4. The handler runs. If it throws, the claim is released and a `500` lets AgentMail retry.

The default deduplicator is in-memory. When running more than one instance, register your own `IAgentMailWebhookDeduplicator` backed by a shared store.

## Webhook sample

`samples/AgentMail.WebhookSample` logs verified events. To receive real AgentMail events on your machine:

1. Expose the sample with [Dev Tunnels](https://learn.microsoft.com/azure/developer/dev-tunnels/):

   ```bash
   winget install Microsoft.devtunnel
   devtunnel user login
   devtunnel host -p 5080 --allow-anonymous
   ```

2. In the AgentMail console, add a webhook for `message.received` pointing at `https://<your-tunnel>/webhooks/agentmail` and copy its signing secret.
3. Store the secret outside the repository and run the sample:

   ```bash
   dotnet user-secrets set "AgentMail:WebhookSecret" "whsec_..." --project samples/AgentMail.WebhookSample
   dotnet run --project samples/AgentMail.WebhookSample
   ```

4. Send an email to your AgentMail inbox. The sample logs the verified event.

## Roadmap

1. Typed messages, threads, attachments, labels, and pagination
2. API error model and rate-limit metadata
3. Resilience hooks and safe retries
4. OpenTelemetry spans/metrics with PII-safe defaults
5. Typed models for delivery, bounce and complaint events
6. Microsoft.Extensions.AI / Semantic Kernel reference integration
7. Regulated-workflow reference application with human approval and audit trail

## License

MIT.
