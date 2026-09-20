# AgentMail .NET

[![CI](https://github.com/ghubnab99/agentmail-dotnet/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/ghubnab99/agentmail-dotnet/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/ghubnab99/agentmail-dotnet?include_prereleases&label=release)](https://github.com/ghubnab99/agentmail-dotnet/releases)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)

Production-oriented community .NET SDK and ASP.NET Core integration for [AgentMail](https://agentmail.to).

> **Status:** early preview. This is an independent community project and is not an official AgentMail SDK.

## Verified against the live API

The full loop has been run against production AgentMail, not just mocks:

| Step | Result |
|---|---|
| Create an inbox with `client_id` | Inbox created. Rerunning returns the same inbox instead of a duplicate |
| Send an email | Delivered to a Gmail inbox, not spam |
| Receive a reply through a webhook | `message.received` arrived over a Dev Tunnel, the Svix signature was verified, and the event was parsed with its message and thread |
| Replay and forgery | A repeated delivery is acknowledged but not processed twice. A forged signature is rejected with `400` |
| Reply in thread | A reply sent to the inbound message id arrived inside the original Gmail thread, with `message.sent` and `message.delivered` following over the webhook |
| Retry with the same `Idempotency-Key` | Returned the original `message_id` and no second email was sent |

### Constraints observed in that run

Behaviour seen on 2026-09-20, recorded because it is not obvious from the SDK surface. These are observations of the live API on that date, not guarantees about future behaviour.

- `Idempotency-Key` was accepted only with the characters `A-Z a-z 0-9 - . _ ~`; anything else came back as `400 validation_error`. `AgentMailClient` now checks this before sending.
- An inbox `display_name` containing parentheses was rejected with `400 validation_error`.
- A reply with no `text` or `html` was accepted and delivered as an empty email.

## Why this exists

AgentMail gives AI agents programmable email inboxes. This project brings that API into the .NET ecosystem with patterns expected in production services: typed clients, cancellation, idempotency, dependency injection, verified webhooks, and a path toward observability and enterprise hardening.

## Current vertical slice

- Create an inbox with `client_id` idempotency
- Retrieve an inbox
- Send email and reply in-thread with optional `Idempotency-Key`
- Bearer authentication
- ASP.NET Core dependency injection
- Svix webhook signature verification (tested against Svix's reference vectors)
- Timestamp tolerance for replay resistance
- Duplicate-delivery protection keyed on `svix-id`, pluggable for shared stores
- Typed `message.received` events (message, thread, attachments)
- Webhook-only hosting without an API key
- CI build, tests, and preview NuGet packing

## Quickstart

Store your API key once with [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets). It stays outside the repository and out of your shell history:

```bash
dotnet user-secrets set "AgentMail:ApiKey" "am_..." --project samples/AgentMail.Quickstart
dotnet run --project samples/AgentMail.Quickstart
```

The sample creates an inbox, or reuses it on later runs. To also send yourself an email:

```bash
dotnet user-secrets set "AgentMail:ToEmail" "you@example.com" --project samples/AgentMail.Quickstart
dotnet run --project samples/AgentMail.Quickstart
```

The `AGENTMAIL_API_KEY` and `AGENTMAIL_TO_EMAIL` environment variables also work, which is useful in CI.

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

## Customer Operations Workflow Demo

`samples/AgentMail.Demo` shows the SDK in a realistic workflow, using payment dispute intake as the example:

1. A customer emails the demo inbox.
2. The verified webhook opens a case, and a rules-based extractor pulls out the amount, merchant and date.
3. The app drafts an acknowledgement, and it is only sent after a person clicks **Approve & Reply**.
4. The reply goes out with `ReplyMessageAsync` in the customer's original thread. Its `Idempotency-Key` is derived from the inbound message, so a retry can never send a second email.
5. A live timeline shows AgentMail's `message.sent` and `message.delivered` events, blocked duplicate deliveries, and rejected webhooks.
6. **Replay same delivery** re-sends the exact captured request. Within 5 minutes it is detected as a duplicate (no second case, no second reply). After 5 minutes it is rejected as stale.

The app listens on two loopback ports. **5080** serves only the webhook, and only this port should be exposed through a tunnel. **5081** serves the dashboard, including its Approve and Replay actions. Replies only go to allowlisted addresses.

```bash
dotnet user-secrets set "AgentMail:ApiKey" "am_..." --project samples/AgentMail.Demo
dotnet user-secrets set "AgentMail:WebhookSecret" "whsec_..." --project samples/AgentMail.Demo
dotnet user-secrets set "Demo:AllowedRecipients:0" "you@example.com" --project samples/AgentMail.Demo
devtunnel host -p 5080 --allow-anonymous
dotnet run --project samples/AgentMail.Demo
```

Then open http://localhost:5081. Subscribe the AgentMail webhook to `message.received`, `message.sent` and `message.delivered`, and send an email to the inbox address shown in the header. The demo and `AgentMail.WebhookSample` both use port 5080, so run one at a time.

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
