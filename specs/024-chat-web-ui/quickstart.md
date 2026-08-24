# Quickstart: Chat Web UI (Stories 1–2 slice)

Validates User Story 1 (sign-in/chat home) and User Story 2 (start conversation, streaming response) end-to-end.

## Prerequisites

- .NET 10 SDK installed.
- `src/EnterpriseAIPlatform.Web/appsettings.Development.json` (or user-secrets) has `PlatformAuthentication:Mode = Development` with a `DevelopmentUser` (Name + Email) configured — this activates `DevelopmentAuthenticationHandler` so no real Entra sign-in is needed locally.
- Backing stores for specs 002/014/004 (Cosmos DB emulator or configured connection strings) reachable, per those specs' existing setup — this slice adds no new infrastructure dependencies.

## Run

```bash
dotnet run --project src/EnterpriseAIPlatform.Web
```

## Validate Story 1 — chat home landing

1. Open the app's root URL in a browser with no session/cookies.
2. **Expected**: routed into the sign-in flow (dev-auth banner + simulated identity in Development mode; Entra challenge in a real environment) before any chat content is visible.
3. After authenticating, **expected**: land on a chat home screen showing your identity (name/email from `DevelopmentUser` or Entra claims) and an empty, ready-to-type composer — not a "Hello, world!" placeholder and not a button you must click first.

## Validate Story 2 — start a conversation and watch it stream

1. Type a message into the composer and submit.
2. **Expected**: the message appears in the transcript immediately; the send action becomes disabled; the assistant's reply appears incrementally (token-by-token), not all at once.
3. Once the reply finishes, **expected**: the send action re-enables.
4. Type and send a follow-up message. **Expected**: it's added to the same transcript, and the assistant's reply reflects the earlier message (same `ThreadId` reused — confirm via logs/telemetry if needed, per `contracts/ui-integration-contract.md`).
5. Trigger a rejection (e.g., configure a very low daily message limit per spec 004/014 test config, or send a message long enough to hit `MESSAGE_TOO_LONG`). **Expected**: a clear, specific reason is shown, and the typed text is not lost.
6. Simulate an interrupted stream (e.g., stop the app mid-response, or induce a cancellation in a debug session). **Expected**: the partial assistant response remains visible and is visually marked as interrupted, not silently completed or removed.

## Out of scope for this quickstart

Conversation list/switch/rename (Story 3), multi-pane model comparison (Story 4), and changelog/version-alert (Story 5) — covered by a later plan pass per `docs/spec-sequencing-plan.md`.
