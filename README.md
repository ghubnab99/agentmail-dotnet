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
- Svix-style webhook signature verification
- Timestamp tolerance for replay resistance
- Typed webhook event envelope
- CI build, tests, and preview NuGet packing

## Quickstart

Set `AGENTMAIL_API_KEY`, then run:

```bash
dotnet run --project samples/AgentMail.Quickstart
```

Set `AGENTMAIL_TO_EMAIL` as well if you want the sample to send a message after creating/reusing its inbox.

## ASP.NET Core

```csharp
builder.Services.AddAgentMail(options =>
{
    options.ApiKey = builder.Configuration["AgentMail:ApiKey"]!;
    options.WebhookSecret = builder.Configuration["AgentMail:WebhookSecret"];
});

app.MapAgentMailWebhook("/webhooks/agentmail", async (evt, ct) =>
{
    if (evt.EventType == "message.received")
    {
        // Queue work; return quickly.
    }

    await Task.CompletedTask;
});
```

Webhook verification uses the raw body plus AgentMail's documented `svix-id`, `svix-timestamp`, and `svix-signature` headers.

## Roadmap

1. Typed messages, threads, attachments, labels, and pagination
2. API error model and rate-limit metadata
3. Resilience hooks and safe retries
4. OpenTelemetry spans/metrics with PII-safe defaults
5. Full typed webhook event models
6. Microsoft.Extensions.AI / Semantic Kernel reference integration
7. Regulated-workflow reference application with human approval and audit trail

## License

MIT.
