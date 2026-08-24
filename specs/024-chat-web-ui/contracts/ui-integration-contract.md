# UI Integration Contract: Chat Web UI (Stories 1–2 slice)

This feature introduces no new HTTP endpoints. Its "contract" is the set of existing Application-layer interfaces the chat home component/state consumes, and the exact call sequence — documented here so the sequence is verifiable independent of implementation.

## Consumed interfaces (all pre-existing, unmodified)

| Interface | Method | Source spec | File |
|---|---|---|---|
| `ICurrentUserAccessor` | `GetCurrentUser() : ServerActionResponse<UserModel>` | 002 | `src/EnterpriseAIPlatform.Application/Identity/ICurrentUserAccessor.cs` |
| `IIdentityHasher` | `ForEmail(string) : Hash` | 002 | `src/EnterpriseAIPlatform.Application/Identity/*` |
| `IModelAccessService` | `GetAvailableModelsAsync(UserModel caller) : Task<IReadOnlyList<ModelConfigDocument>>` | 014 | `src/EnterpriseAIPlatform.Application/ModelAccess/IModelAccessService.cs` |
| `IChatThreadStore` | `CreateAsync(partitionKey, ownerEmail, modelId, ct) : Task<ChatThreadModel>` | 004 | `src/EnterpriseAIPlatform.Application/Chat/IChatThreadStore.cs` |
| `IChatPipeline` | `SendMessageAsync(UserModel caller, string threadId, string userText, string requestedModelId, CancellationToken ct) : Task<ChatSendResult>` | 004 | `src/EnterpriseAIPlatform.Application/Chat/IChatPipeline.cs` |

## Call sequence — first message in a new conversation (Story 1 → Story 2)

1. On render, component resolves `ICurrentUserAccessor.GetCurrentUser()`. (Expected to always be `OK` here — the fallback authorization policy already blocks unauthenticated requests before this component renders; see research.md.)
2. User types into the composer and submits (Enter or a Send action) while `IsStreaming == false`.
3. If `ThreadId` is null:
   a. Call `IModelAccessService.GetAvailableModelsAsync(caller)`; take the first entry's `Id` as `ModelId`.
   b. Call `IChatThreadStore.CreateAsync(identityHasher.ForEmail(caller.Email).Value, caller.Email, modelId, ct)`; store the returned `ChatThreadModel.Id` as `ThreadId`.
4. Append a `ChatMessageViewState { Role = "user", Content = <composer text>, IsComplete = true }` to `Messages`; clear the composer; set `IsStreaming = true`.
5. Call `IChatPipeline.SendMessageAsync(caller, ThreadId, text, ModelId, ct)`.
6. Switch on the returned `ChatSendResult`:
   - `Rejected(code, resetsAt)` → set `ErrorMessage` to the code-specific user-facing text (mirrors `ChatEndpoints.MapRejection`'s cases, minus the HTTP status); `IsStreaming = false`; composer text is restored so the user can retry (FR-004).
   - `ContentBlocked(category)` → set `ErrorMessage` accordingly; `IsStreaming = false`; composer text restored.
   - `Streaming(chunks)` → append a new empty assistant `ChatMessageViewState { IsComplete = false }`, then `await foreach` `chunks`, appending each token to that message's `Content` and calling `StateHasChanged()` after each. On normal completion, set `IsComplete = true`. On cancellation/exception during enumeration, set `IsInterrupted = true`, `IsComplete = true` (stop treating it as in-progress), and set a non-alarming `ErrorMessage`.
   - Unhandled exception from `SendMessageAsync` itself → generic `ErrorMessage` (no internal detail), `IsStreaming = false` (mirrors `ChatEndpoints.cs`'s catch-all 500 handling).
7. `IsStreaming = false` once the stream ends (normally or interrupted), re-enabling the composer.

## Follow-up message (same conversation, Story 2 scenario 3)

Repeats steps 2, 4–7 above with the existing `ThreadId`/`ModelId` (step 3 is skipped since a thread already exists).
