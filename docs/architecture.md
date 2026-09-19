# Architecture

The first preview intentionally stays small and composable.

```text
Application
   |
   +-- AgentMail.Client
   |      +-- typed REST operations
   |      +-- bearer authentication
   |      +-- cancellation
   |      +-- send idempotency
   |
   +-- AgentMail.AspNetCore
          +-- dependency injection
          +-- verified Svix-style webhooks
          +-- timestamp tolerance
          +-- typed event envelope
```

The initial vertical slice covers inbox creation, inbox retrieval, message sending, ASP.NET Core registration, and verified webhook ingestion.

The implementation follows AgentMail's documented production API base URL, bearer authentication, `client_id` idempotency for resource creation, `Idempotency-Key` for message sends, and Svix-style webhook headers.
