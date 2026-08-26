# Quickstart: Chat Web UI (Stories 1–3)

Validates User Story 1 (sign-in/chat home), User Story 2 (start conversation, streaming response), and User Story 3 (conversation list, switch, rename) end-to-end.

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

## Validate Story 3 — conversation list, switch, rename

1. From the chat home screen (`/`), send a message to start a first conversation, then navigate back to `/` and send a message to start a second conversation.
2. **Expected**: a sidebar conversation list is visible alongside the composer, showing both conversations, most-recently-active one first (the second conversation, since it was created/messaged most recently).
3. Click the first (older) conversation in the sidebar. **Expected**: the URL changes to `/chat/{its-id}`, its prior messages load into the transcript before you send anything new, and the sidebar remains visible.
4. Send a follow-up message in this conversation. **Expected**: it's added to this same conversation (not the other one), and the sidebar reorders so this conversation is now first (most-recently-active).
5. Click the conversation's name in the sidebar to rename it, type a new name, and press Enter. **Expected**: the sidebar shows the new name immediately; reload the page and confirm the new name persisted.
6. Try renaming a conversation to an empty or whitespace-only name. **Expected**: the rename is rejected with a clear message, and the prior name remains.
7. Manually navigate to `/chat/{a-made-up-id}` (or a real ID copied while signed in as a different user, if you have two test accounts). **Expected**: a clear not-found state renders, the sidebar remains visible and unaffected, and no other user's content is ever shown.
8. Sign in with zero prior conversations (a fresh test user). Open the sidebar. **Expected**: an empty state inviting you to start a conversation, not an error.
9. Navigate to `/` after having viewed a conversation. **Expected**: `/` always shows a fresh, blank, ready-to-type composer — it never auto-resumes the last-viewed conversation.

## Out of scope for this quickstart

Multi-pane model comparison (Story 4) and changelog/version-alert (Story 5) — covered by a later plan pass per `docs/spec-sequencing-plan.md`.
