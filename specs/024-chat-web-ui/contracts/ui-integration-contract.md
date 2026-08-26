# UI Integration Contract: Chat Web UI (Stories 1–3)

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

## Story 3: additional consumed interfaces

| Interface | Method | File |
|---|---|---|
| `IChatThreadStore` | `ListByOwnerAsync(ownerPartitionKey, ct) : Task<IReadOnlyList<ChatThreadModel>>` (Story 3) | `Application/Chat/IChatThreadStore.cs` |
| `IChatThreadStore` | `RenameAsync(threadId, ownerPartitionKey, newDisplayName, ct) : Task<ServerActionResponse<ChatThreadModel>>` (Story 3) | same |
| `IChatThreadStore` | `TouchLastActivityAsync(threadId, ownerPartitionKey, ct) : Task` (Story 3) | same |
| `IChatMessageStore` | `ListByThreadAsync(threadId, ownerPartitionKey, ct) : Task<IReadOnlyList<ChatMessageModel>>` (Story 3) | `Application/Chat/IChatMessageStore.cs` |

## Story 3: call sequence — loading the conversation list (sidebar, on every page render)

1. `ConversationListState.LoadConversationsAsync()` calls `IChatThreadStore.ListByOwnerAsync(identityHasher.ForEmail(caller.Email).Value, ct)`.
2. Result (already sorted most-recently-active-first by the store) populates `Conversations`.
3. If empty, `ConversationList.razor` renders the empty-state invitation (AC4).

## Story 3: call sequence — switching to a conversation

1. User clicks a conversation in the sidebar (or navigates directly to `/chat/{id}`) → `NavigationManager.NavigateTo($"/chat/{id}")` (a no-op if already there).
2. `ChatConversation.razor` → `ChatShell.razor` → `ChatComposerState.SwitchToAsync(threadId)`:
   a. `IChatThreadStore.GetAsync(threadId, partitionKey, ct)` — ownership-scoped point read.
   b. If `null` → `IsNotFound = true`, `Messages` cleared, `ThreadId = null`; `ChatShell` renders the not-found block (sidebar stays visible) — satisfies FR-013.
   c. If found → `ThreadId`/`ModelId` set from the thread; `IChatMessageStore.ListByThreadAsync(threadId, partitionKey, ct)` loads history into `Messages` (each entry `IsComplete = true`, `IsInterrupted = false`).
3. Sending a follow-up from here repeats the Stories 1–2 send sequence unchanged (`ThreadId`/`ModelId` already set, so thread creation is skipped).

## Story 3: call sequence — renaming a conversation (inline edit)

1. User clicks a conversation's name in the sidebar → it becomes an editable field (client-side UI state only, no call yet).
2. On Enter/blur with changed text: `ConversationListState.RenameAsync(threadId, newText)` → `IChatThreadStore.RenameAsync(threadId, partitionKey, newText, ct)`.
3. `ServerActionResponse.Status`:
   - `OK` → update the matching entry in `Conversations` with the new `DisplayName`; exit edit mode.
   - `ERROR` (empty/whitespace after trim) → `RenameErrorMessage` set, edit field stays open with the prior name restored (per Edge Cases: "leaving the previous name in place").
   - `NOT_FOUND` → treated the same as `ERROR` for display purposes (this thread disappeared from under the user — an edge case, not a named acceptance scenario).
4. On Escape: discard the edit, no call made.
