# Phase 1 Data Model: Chat Web UI (Stories 1–5)

No new persisted entities are introduced by this slice — all persistence continues through spec 004's existing `ChatThreadModel` / `ChatMessageModel` via `IChatThreadStore` / `IChatPipeline`. This slice only adds **client-side, per-circuit view-state** that lives in Blazor component/service memory and is discarded when the circuit ends (page reload restarts Story 2 with a fresh empty composer — full history reload across page loads is Story 3, deferred).

## ChatComposerState (new, Web-layer, scoped per circuit)

The state coordinator injected into the chat home component. Not persisted; not an entity in the domain sense — a UI view-model.

| Field | Type | Notes |
|---|---|---|
| `ThreadId` | `string?` | Null until the first message is sent; set from `ThreadResponse`/`ChatThreadModel.Id` returned by `IChatThreadStore.CreateAsync`. |
| `ModelId` | `string?` | Resolved once at first send via `IModelAccessService.GetAvailableModelsAsync` (first entitled model — see research.md); reused for the rest of the circuit's conversation. |
| `Messages` | `List<ChatMessageViewState>` | Ordered transcript for the current circuit; append-only. |
| `ComposerText` | `string` | Two-way bound to the input; cleared on successful send. |
| `IsStreaming` | `bool` | `true` from send-start until the stream completes, errors, or is interrupted; drives the send-disabled clarification (FR-002). |
| `ErrorMessage` | `string?` | Set to a rejection reason (FR-004: rate limit / content-blocked text) or a generic message (unexpected failure); cleared on next successful send attempt. |

## ChatMessageViewState (new, Web-layer)

| Field | Type | Notes |
|---|---|---|
| `Role` | `"user"` \| `"assistant"` | Mirrors `ChatMessageModel.Role`. |
| `Content` | `string` | For an in-progress assistant message, appended to incrementally as chunks arrive. |
| `IsInterrupted` | `bool` | Set `true` if the stream ends via cancellation/exception rather than the pipeline's normal completion — satisfies FR-012 (partial response stays visible, marked interrupted). |
| `IsComplete` | `bool` | `true` once the assistant message finishes streaming normally; used to stop appending and to distinguish "still streaming" from "done" in rendering. |

## Reused existing entities (no changes for Stories 1–2)

- `ChatThreadModel` (`src/EnterpriseAIPlatform.Domain/Chat/ChatThreadModel.cs`) — `Id, PartitionKey, OwnerUserId, Version, ModelId, CreatedAtUtc, DataProducts`. **(Story 3: gains two fields — see below.)**
- `ChatMessageModel` (`src/EnterpriseAIPlatform.Domain/Chat/ChatMessageModel.cs`) — `Id, PartitionKey, ThreadId, Role, Content, CreatedAtUtc`.
- `ChatSendResult` (`Application/Chat`) — `Rejected(PreflightRejectionCode, DateTimeOffset?)`, `ContentBlocked(string Category)`, `Streaming(IAsyncEnumerable<string> Chunks)` — the component switches on this exactly as `ChatEndpoints.cs` does, mapping each case to `ErrorMessage`/streaming state instead of an HTTP response.

## Story 3 additions

### `ChatThreadModel` — new fields (backward-compatible addition, no migration needed for new documents)

| Field | Type | Notes |
|---|---|---|
| `DisplayName` | `string` (required) | Set at creation to a creation-timestamp-based default (e.g. `"Conversation — Aug 26, 2026 3:41 PM"`, per Clarifications); renamable thereafter via `RenameAsync`. Never empty/whitespace (enforced by `ConversationRenameRules`). |
| `LastActivityAtUtc` | `DateTimeOffset` | Initialized to `CreatedAtUtc`; bumped to `DateTimeOffset.UtcNow` by `TouchLastActivityAsync`, called from `ChatPipeline` after every successful send. Drives the list's most-recently-active-first order (FR-005). |

`ChatCosmosDocuments.cs`'s `ChatThreadDocument` gains matching fields plus `FromModel`/`ToModel` updates.

### `ConversationRenameRules` (new, Application layer, static)

Framework-free validator, mirroring the existing `MultiChatQuadrantRules` pattern: `TryValidate(string? candidate, out string trimmed)` trims and rejects empty/whitespace-only input. Called from `IChatThreadStore.RenameAsync` — not duplicated in the Blazor component or the HTTP endpoint (Constitution Principle V).

### `ConversationListState` (new, Web-layer, scoped per circuit)

| Field | Type | Notes |
|---|---|---|
| `Conversations` | `List<ChatThreadModel>` | Loaded via `IChatThreadStore.ListByOwnerAsync`; already sorted most-recently-active-first by the store. |
| `IsLoading` | `bool` | True while `LoadConversationsAsync` is in flight. |
| `RenameErrorMessage` | `string?` | Set when a rename attempt is rejected (empty/whitespace); surfaced next to the inline edit field. |

Methods: `LoadConversationsAsync()`, `RenameAsync(threadId, newName)` (calls `IChatThreadStore.RenameAsync`, updates the matching in-memory entry on success), and selection is a plain `NavigationManager.NavigateTo($"/chat/{id}")` call from the component, not a state-class method (Story 3's routing decision — see research.md).

### `ChatComposerState` — Story 3 additions

| Addition | Notes |
|---|---|
| `IsNotFound` | `bool`, set by `SwitchToAsync` when the given thread ID doesn't resolve to one of the caller's own threads. |
| `SwitchToAsync(threadId)` | Loads the thread via the existing ownership-scoped `GetAsync`, then its full history via the new `IChatMessageStore.ListByThreadAsync`, replacing `Messages` (each loaded message becomes an already-`IsComplete = true` `ChatMessageViewState`). |
| `ResetToNewAsync()` | Clears `ThreadId`/`Messages`/`ComposerText`/`ErrorMessage` — used when navigating to `/` after having viewed a conversation. |
| New constructor dependency | `IChatMessageStore` (not previously needed — sending never read history back before Story 3). |

## Reused existing entities (Story 3, no changes)

- `IChatThreadStore.GetAsync` — reused as-is for `SwitchToAsync`'s ownership-scoped lookup and as the first half of `RenameAsync`'s read-modify-write.
- `ServerActionResponse<T>` (`Application/Common`) — `RenameAsync`'s return shape (`OK`/`NotFound`/`Error`), same envelope used throughout specs 002/004/006/014.

## Stories 4–5 additions

No new persisted entities. All state below is client-side, per-circuit view-state (same nature as `ChatComposerState`/`ConversationListState`); all persistence continues through spec 006's existing `MultiChatSession` Cosmos doc and spec 017's existing `VersionAcknowledgmentModel` Cosmos doc / file-system-backed `ChangelogEntry` reads.

### `CompareSessionState` (new, Web-layer, scoped per circuit)

| Field | Type | Notes |
|---|---|---|
| `Panes` | `List<ComparePaneViewState>` | Projected from `MultiChatSession.Quadrants` on load; one entry per quadrant, in `Position` order. |
| `AvailableModels` | `IReadOnlyList<ModelConfigDocument>` | Loaded once via `IModelAccessService.GetAvailableModelsAsync`; source of every pane's model-picker options (research.md). |
| `ComposerText` | `string` | Single shared composer, bound in `CompareBoard.razor`. |
| `IsSending` | `bool` | `true` from `SendToAllAsync` start until every dispatched pane's stream reaches `Done`/`Error`; disables the shared send action while `true`. |
| `PaneErrorMessage` | `string?` | Set when `AddQuadrantAsync`/`RemoveQuadrantAsync` is refused (cap/floor) or fails; cleared on the next successful pane-count change. |

### `ComparePaneViewState` (new, Web-layer)

| Field | Type | Notes |
|---|---|---|
| `Position` | `int` | Mirrors `MultiChatQuadrant.Position` (0-based). |
| `ModelId` | `string?` | Mirrors `MultiChatQuadrant.ModelId`; `null` = unassigned — drives the "nothing to send to" indicator (Edge Cases). |
| `ModelDisplayName` | `string?` | Looked up from `CompareSessionState.AvailableModels` for display; `null` when `ModelId` is `null`. |
| `ThreadId` | `string?` | Mirrors `MultiChatQuadrant.ThreadId`; set once the dispatcher creates a thread for this quadrant on first send. |
| `Messages` | `List<ChatMessageViewState>` | Reuses the existing `ChatMessageViewState` type/shape (Stories 1–2) unchanged — lets `ComparePane.razor` reuse `ChatTranscript.razor` directly with no new render component. |
| `ErrorMessage` | `string?` | Set when this pane's `QuadrantEvent.Kind == Error` arrives; does not affect other panes. |

### `UpdateBannerState` (new, Web-layer, scoped per circuit)

| Field | Type | Notes |
|---|---|---|
| `ShowAlert` | `bool` | Set from `AlertWindowEvaluator.ShouldShowAlert(latest, acknowledgment, now)` on init. |
| `LatestVersion` | `string?` | The latest `ChangelogEntry.Version.ToString()`, used in the banner text and as the value persisted on dismiss. |
| `IsDismissed` | `bool` | Set optimistically by `DismissAsync` before the persist call resolves; reverted to `false` if `IVersionAcknowledgmentStore.SetAsync` throws (Constitution Principle III). |

## Reused existing entities (Stories 4–5, no changes)

- `MultiChatSession` / `MultiChatQuadrant` (`Domain/Chat`, spec 006) — read via `IMultiChatSessionStore.GetOrCreateAsync`; quadrant count invariant `[2, 4]` enforced entirely by the existing `MultiChatQuadrantRules`, never re-validated in the UI layer.
- `QuadrantEvent` (`Domain/Chat`, spec 006) — `MultiChatDispatcher.DispatchAsync`'s streamed event type; consumed as-is by `CompareSessionState.SendToAllAsync`.
- `ChangelogEntry` / `VersionAcknowledgmentModel` (`Domain/Support`, spec 017) — read via `IChangelogReader.GetEntriesAsync` / `IVersionAcknowledgmentStore.GetAsync`/`SetAsync`.
- `ModelConfigDocument` (`Domain/ModelAccess`, spec 014) — same type Stories 1–2 already consume for default-model resolution, reused here for the per-pane picker's options.
