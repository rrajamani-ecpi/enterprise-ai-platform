# Quickstart: Chat Web UI (Stories 1–5)

Validates User Story 1 (sign-in/chat home), User Story 2 (start conversation, streaming response), User Story 3 (conversation list, switch, rename), User Story 4 (multi-pane comparison), and User Story 5 (changelog + version-update banner) end-to-end.

## Prerequisites

- .NET 10 SDK installed.
- `src/EnterpriseAIPlatform.Web/appsettings.Development.json` (or user-secrets) has `PlatformAuthentication:Mode = Development` with a `DevelopmentUser` (Name + Email) configured — this activates `DevelopmentAuthenticationHandler` so no real Entra sign-in is needed locally.
- Backing stores for specs 002/014/004/006/017 (Cosmos DB emulator or configured connection strings) reachable, per those specs' existing setup — this slice adds no new infrastructure dependencies.
- At least one `content/changelog/*.md` entry present (per spec 017) to validate Story 5's non-empty changelog/banner paths; remove/rename them temporarily to validate the empty-state paths.

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

## Validate Story 4 — multi-pane model comparison

1. Click "Compare" in the sidebar (visible from any chat page). **Expected**: navigates to `/compare`, sidebar remains visible, and 2 empty (unassigned) panes are shown by default for a first-time visit.
2. Assign a different model to each of the 2 panes via each pane's model picker. Reload the page. **Expected**: both assignments persist across the reload.
3. Type one message in the shared composer and send it. **Expected**: the same message is dispatched to both panes without retyping; each pane's response streams in independently.
4. Simulate one pane being slower or failing (e.g., temporarily misconfigure one assigned model). **Expected**: the other pane's response completes and displays without waiting for the slow/failing one; the failing pane shows a clear error while the other continues normally.
5. At the 2-pane minimum, remove a pane. **Expected**: its model assignment clears but the pane itself remains — pane count stays at 2.
6. Add panes up to 4, then attempt a 5th. **Expected**: the request is refused with a clear reason; pane count stays at 4.
7. Send a message while a pane has no model assigned. **Expected**: that pane visibly indicates it has nothing to send to, rather than silently doing nothing.

## Validate Story 5 — changelog and version-update notice

1. With at least one changelog entry present, click "Changelog" in the sidebar. **Expected**: navigates to `/changelog`, sidebar remains visible, entries render newest-first.
2. Temporarily remove all changelog entries and reopen `/changelog`. **Expected**: a defined empty state renders, not an error. Restore the entries afterward.
3. As a fresh test user (or one whose acknowledged version is older than the latest entry), load any authenticated page. **Expected**: a dismissible update notice appears as a global banner, visible regardless of which page (chat home, a conversation, compare, changelog) is loaded first.
4. Dismiss the notice, then reload the page. **Expected**: the notice does not reappear.
5. (Optional, to confirm fail-loud behavior) Simulate the acknowledgment persist call failing (e.g., a debug breakpoint/temporary fault injection). **Expected**: dismissing the notice does not silently succeed — the banner reappears rather than staying hidden on a failed persist.
