# Phase 1 Data Model: Chat Web UI (Stories 1–2 slice)

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

## Reused existing entities (no changes)

- `ChatThreadModel` (`src/EnterpriseAIPlatform.Domain/Chat/ChatThreadModel.cs`) — `Id, PartitionKey, OwnerUserId, Version, ModelId, CreatedAtUtc, DataProducts`.
- `ChatMessageModel` (`src/EnterpriseAIPlatform.Domain/Chat/ChatMessageModel.cs`) — `Id, PartitionKey, ThreadId, Role, Content, CreatedAtUtc`.
- `ChatSendResult` (`Application/Chat`) — `Rejected(PreflightRejectionCode, DateTimeOffset?)`, `ContentBlocked(string Category)`, `Streaming(IAsyncEnumerable<string> Chunks)` — the component switches on this exactly as `ChatEndpoints.cs` does, mapping each case to `ErrorMessage`/streaming state instead of an HTTP response.
