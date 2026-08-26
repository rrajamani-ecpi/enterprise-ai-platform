# Tasks: Chat Web UI — Stories 1–3

**Input**: Design documents from `/specs/024-chat-web-ui/` (plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md)

**Scope**: User Story 1 (sign-in/chat home), User Story 2 (start conversation, streaming response) — both complete and implemented (Phases 1–5 below) — plus **User Story 3** (conversation list, switch, rename), added in this pass (Phase 6). Stories 4–5 (multi-pane compare, changelog) remain deferred to a later tasks pass.

**Tests**: Included — plan.md's Testing section and Project Structure explicitly call out test files per story as part of this feature's deliverables.

**Organization**: Tasks are grouped by user story. Stories 1–2 are both Priority P1 and were sequenced (Story 1 is a hard prerequisite for Story 2). **Story 3 is Priority P2** and depends on Stories 1–2's `ChatComposerState`/`ChatComposer`/`ChatTranscript` already existing, but is otherwise a self-contained addition (list/switch/rename don't change Stories 1–2's send/stream behavior). Because Story 3 spans four architectural layers (Domain → Application → Infrastructure → Web), its tasks are grouped **by layer** rather than in one flat "tests then implementation" block — each layer's test tasks (where a test convention exists for that layer) are co-located with that layer's implementation tasks, still tagged `[US3]` throughout.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no unmet dependency)
- **[Story]**: US1, US2, or US3, per spec.md

## Path Conventions

Single existing solution — `src/EnterpriseAIPlatform.Web/` (presentation) on top of the existing `Application`/`Infrastructure`/`Domain` projects; tests under `tests/EnterpriseAIPlatform.{UnitTests,IntegrationTests}/`. No new projects (see plan.md's Structure Decision).

---

## Phase 1: Setup

**Purpose**: Add the one new dependency this feature needs.

- [X] T001 Add a `bUnit` package reference to `tests/EnterpriseAIPlatform.UnitTests/EnterpriseAIPlatform.UnitTests.csproj`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared state class both stories bind to. **Must complete before either story.**

- [X] T002 [P] Create `ChatMessageViewState` record (`Role`, `Content`, `IsInterrupted`, `IsComplete`) in `src/EnterpriseAIPlatform.Web/Services/ChatMessageViewState.cs`, per data-model.md
- [X] T003 Create `ChatComposerState` class in `src/EnterpriseAIPlatform.Web/Services/ChatComposerState.cs` with fields `ThreadId`, `ModelId`, `Messages` (`List<ChatMessageViewState>`), `ComposerText`, `IsStreaming`, `ErrorMessage`, and a constructor injecting `ICurrentUserAccessor`, `IIdentityHasher`, `IChatThreadStore`, `IChatPipeline`, `IModelAccessService` (depends on T002)
- [X] T004 Register `ChatComposerState` as a `Scoped` service in `src/EnterpriseAIPlatform.Web/Program.cs` (depends on T003)

**Checkpoint**: Foundation ready — User Story 1 can now begin.

---

## Phase 3: User Story 1 - Sign in and land on a working chat home (Priority: P1) 🎯 MVP

**Goal**: An authenticated user lands on a real chat home screen identifying them, with an empty, ready-to-type composer — no separate creation step, no placeholder text. An unauthenticated visitor is routed into sign-in first.

**Independent Test**: Sign in and confirm the landing screen shows your identity and a ready composer, not "Hello, world!"; confirm an unauthenticated request never reaches chat content.

### Tests for User Story 1

- [X] T005 [P] [US1] Integration test: an unauthenticated request to `/` is challenged/redirected and never renders chat content, in `tests/EnterpriseAIPlatform.IntegrationTests/Web/ChatHomeAuthTests.cs` (uses `WebApplicationFactory`, mirroring `ChatSendMessageTests.cs`'s setup pattern)

### Implementation for User Story 1

- [X] T006 [US1] Add an identity accessor (resolve `ICurrentUserAccessor.GetCurrentUser()`, expose the caller's display name/email) to `src/EnterpriseAIPlatform.Web/Services/ChatComposerState.cs` (depends on T003)
- [X] T007 [P] [US1] Create `ChatComposer.razor` (a bound textbox + submit action; disabled-state binding wired in US2) in `src/EnterpriseAIPlatform.Web/Components/Chat/ChatComposer.razor` (depends on T006)
- [X] T008 [P] [US1] Create `ChatTranscript.razor` (renders `ChatComposerState.Messages`; empty by default) in `src/EnterpriseAIPlatform.Web/Components/Chat/ChatTranscript.razor` (depends on T006)
- [X] T009 [US1] Replace the placeholder content of `src/EnterpriseAIPlatform.Web/Components/Pages/Home.razor` with a chat home layout: inject `ChatComposerState`, show an identity header, render `ChatTranscript` + `ChatComposer` (depends on T007, T008)
- [X] T010 [US1] bUnit test: rendering `Home.razor` for an authenticated user shows the identity header and an empty composer, not placeholder text, in `tests/EnterpriseAIPlatform.UnitTests/Web/ChatComposerComponentTests.cs` (depends on T009)

**Checkpoint**: User Story 1 is independently testable — a real, non-placeholder screen exists and is reachable only when authenticated.

---

## Phase 4: User Story 2 - Start a conversation and watch the response stream in (Priority: P1)

**Goal**: Sending a message from the Story 1 screen creates a conversation, streams the assistant's reply token-by-token, disables sending while streaming, surfaces rejections/errors clearly with input recoverable, and keeps a follow-up message in the same conversation. An interrupted stream stays visible and marked as such.

**Independent Test**: Send a message and confirm the reply renders incrementally; send a follow-up and confirm it continues the same conversation; trigger a rejection and an interruption and confirm both are handled per FR-004/FR-012.

### Tests for User Story 2

- [X] T011 [P] [US2] Unit tests (NSubstitute) for `ChatComposerState`'s send flow — first send creates a thread via `IChatThreadStore.CreateAsync` with the first entitled model, second send reuses the cached `ThreadId`, `IsStreaming` toggles around the call, assistant chunks append incrementally, and `Rejected`/`ContentBlocked`/unhandled-exception/interrupted-enumeration cases are each handled per contracts/ui-integration-contract.md — in `tests/EnterpriseAIPlatform.UnitTests/Web/ChatComposerStateTests.cs`
- [X] T012 [P] [US2] bUnit test: the composer's send control is disabled while `IsStreaming` is true and re-enabled after, and an interrupted message renders with a distinct visual marker, in `tests/EnterpriseAIPlatform.UnitTests/Web/ChatComposerComponentTests.cs`

### Implementation for User Story 2

- [X] T013 [US2] Implement default-model resolution — call `IModelAccessService.GetAvailableModelsAsync(caller)` and cache the first entry's Id as `ModelId` — in `src/EnterpriseAIPlatform.Web/Services/ChatComposerState.cs` (depends on T006)
- [X] T014 [US2] Implement lazy thread creation — on first send, call `IChatThreadStore.CreateAsync(...)` and cache the returned `ChatThreadModel.Id` as `ThreadId`; reuse it on subsequent sends — in `ChatComposerState.cs` (depends on T013)
- [X] T015 [US2] Implement `SendAsync`: append the user message, set `IsStreaming = true`, call `IChatPipeline.SendMessageAsync`, and switch on the returned `ChatSendResult` (`Rejected` → set `ErrorMessage` + restore composer text; `ContentBlocked` → same; unhandled exception → generic `ErrorMessage`) per contracts/ui-integration-contract.md, in `ChatComposerState.cs` (depends on T014)
- [X] T016 [US2] Implement streaming chunk consumption for the `Streaming` case: `await foreach` the chunks appending to the in-progress assistant `ChatMessageViewState.Content`, mark `IsComplete = true` on normal finish, and on cancellation/exception during enumeration mark `IsInterrupted = true` / `IsComplete = true` with a non-alarming `ErrorMessage`; reset `IsStreaming = false` in all end states — in `ChatComposerState.cs` (depends on T015)
- [X] T017 [P] [US2] Wire `ChatComposer.razor`'s submit action to `ChatComposerState.SendAsync`, bind the send control's disabled state to `IsStreaming`, and surface `ErrorMessage` with the composer text preserved for retry — in `src/EnterpriseAIPlatform.Web/Components/Chat/ChatComposer.razor` (depends on T016)
- [X] T018 [P] [US2] Update `ChatTranscript.razor` to visually distinguish an in-progress (`!IsComplete`) message from a completed one and an interrupted (`IsInterrupted`) one from a normal completion — in `src/EnterpriseAIPlatform.Web/Components/Chat/ChatTranscript.razor` (depends on T016)

**Checkpoint**: User Stories 1 and 2 both work end-to-end — the full quickstart.md flow is executable.

---

## Phase 5: Polish

- [X] T019 Run `quickstart.md` end-to-end against a local `dotnet run` (Development auth mode) and confirm every validation step in both its Story 1 and Story 2 sections passes

---

## Phase 6: User Story 3 - See, switch between, and rename past conversations (Priority: P2)

**Goal**: A user with multiple prior conversations sees them listed (most-recently-active first), can switch into any of them (loading its full history before sending anything new), and can rename one inline — with an empty-state for no conversations and a not-found state for a foreign/nonexistent conversation ID reached via direct navigation.

**Independent Test**: Create two or more conversations, confirm both appear in a list scoped to the current user, switch into an older one and confirm its prior messages render, rename it and confirm the new name persists across a page reload — per spec.md's Story 3 Independent Test and quickstart.md's Story 3 section.

### Domain & Application layer

- [X] T020 [US3] Add `DisplayName` (`required string`) and `LastActivityAtUtc` (`DateTimeOffset`) fields to `ChatThreadModel` in `src/EnterpriseAIPlatform.Domain/Chat/ChatThreadModel.cs`, per data-model.md
- [X] T021 [P] [US3] Create `ConversationRenameRules` static validator (`TryValidate(string? candidate, out string trimmed)` — trims and rejects empty/whitespace-only input, mirroring the existing `MultiChatQuadrantRules` pattern) in `src/EnterpriseAIPlatform.Application/Chat/ConversationRenameRules.cs`
- [X] T022 [P] [US3] Unit test for `ConversationRenameRules.TryValidate` (empty string, whitespace-only, valid name, leading/trailing-whitespace trimmed) in `tests/EnterpriseAIPlatform.UnitTests/ConversationRenameRulesTests.cs` (depends on T021)
- [X] T023 [US3] Add `ListByOwnerAsync(string ownerPartitionKey, CancellationToken ct = default) : Task<IReadOnlyList<ChatThreadModel>>`, `RenameAsync(string threadId, string ownerPartitionKey, string newDisplayName, CancellationToken ct = default) : Task<ServerActionResponse<ChatThreadModel>>`, and `TouchLastActivityAsync(string threadId, string ownerPartitionKey, CancellationToken ct = default) : Task` to `IChatThreadStore` in `src/EnterpriseAIPlatform.Application/Chat/IChatThreadStore.cs` (depends on T020, T021)
- [X] T024 [US3] Add `ListByThreadAsync(string threadId, string ownerPartitionKey, CancellationToken ct = default) : Task<IReadOnlyList<ChatMessageModel>>` to `IChatMessageStore` in `src/EnterpriseAIPlatform.Application/Chat/IChatMessageStore.cs`

### Infrastructure (Cosmos) layer

- [X] T025 [US3] Add `DisplayName`/`LastActivityAtUtc` fields to `ChatThreadDocument` and update its `FromModel`/`ToModel` in `src/EnterpriseAIPlatform.Infrastructure/Chat/ChatCosmosDocuments.cs` (depends on T020)
- [X] T026 [US3] Update `CosmosChatThreadStore.CreateAsync` to set a creation-timestamp-based default `DisplayName` (e.g. `"Conversation — {CreatedAtUtc:MMM d, yyyy h:mm tt}"`) and `LastActivityAtUtc = CreatedAtUtc` in `src/EnterpriseAIPlatform.Infrastructure/Chat/CosmosChatThreadStore.cs` (depends on T025)
- [X] T027 [US3] Implement `CosmosChatThreadStore.ListByOwnerAsync` via `Container.GetItemQueryIterator<ChatThreadDocument>` with `QueryRequestOptions.PartitionKey` set to the caller's partition key (single-partition, non-fan-out query), sorted client-side by `LastActivityAtUtc` descending, in `CosmosChatThreadStore.cs` (depends on T023, T025)
- [X] T028 [US3] Implement `CosmosChatThreadStore.RenameAsync`: validate via `ConversationRenameRules.TryValidate` (→ `ServerActionResponse.Error` if invalid), point-read via the existing ownership-scoped `GetAsync` (→ `ServerActionResponse.NotFound` if null), then `Container.UpsertItemAsync` the updated document (→ `ServerActionResponse.Ok`), in `CosmosChatThreadStore.cs` (depends on T021, T026)
- [X] T029 [US3] Implement `CosmosChatThreadStore.TouchLastActivityAsync` (same ownership-scoped read + `Container.UpsertItemAsync` shape as `RenameAsync`, setting `LastActivityAtUtc = DateTimeOffset.UtcNow`) in `CosmosChatThreadStore.cs` (depends on T026)
- [X] T030 [US3] Implement `CosmosChatMessageStore.ListByThreadAsync` via the same single-partition `GetItemQueryIterator` shape (filtered additionally by `ThreadId`), sorted client-side by `CreatedAtUtc` ascending, in `src/EnterpriseAIPlatform.Infrastructure/Chat/CosmosChatMessageStore.cs` (depends on T024)
- [X] T031 [US3] Call `IChatThreadStore.TouchLastActivityAsync` from `ChatPipeline` after both the user and assistant messages are persisted, in `src/EnterpriseAIPlatform.Infrastructure/Chat/ChatPipeline.cs` (depends on T029)

### Test fakes (shared by integration tests)

- [X] T032 [US3] Add `ListByOwnerAsync`, `RenameAsync` (calling the same `ConversationRenameRules` validation), and `TouchLastActivityAsync` to `FakeChatThreadStore`, and `ListByThreadAsync` to `FakeChatMessageStore`, in `tests/EnterpriseAIPlatform.IntegrationTests/FakeChatStores.cs` (depends on T023, T024)

### HTTP endpoints

- [X] T033 [P] [US3] Add `GET /api/chat/threads` (→ `ConversationSummaryResponse[]`), `GET /api/chat/threads/{id}/messages` (→ `MessageResponse[]`, 404 for a foreign/nonexistent thread), and `PATCH /api/chat/threads/{id}` (body `RenameThreadRequest(string DisplayName)` → 200/400 `EMPTY_NAME`/404) to `src/EnterpriseAIPlatform.Web/Endpoints/Chat/ChatEndpoints.cs`, per contracts/chat-threads-http-contract.md (depends on T032)
- [X] T034 [P] [US3] Integration tests: list scoped to owner (another user's conversations never appear), rename (persists / rejected-when-empty-or-whitespace / 404-for-wrong-owner), message history (seeded messages returned in order / 404-for-wrong-owner), in `tests/EnterpriseAIPlatform.IntegrationTests/ChatConversationListTests.cs` (depends on T033)

### Web/Blazor state

- [X] T035 [US3] Add an `IChatMessageStore` constructor dependency, an `IsNotFound` property, `SwitchToAsync(string threadId, CancellationToken ct = default)` (ownership-scoped `GetAsync`; on found, sets `ThreadId`/`ModelId` and loads history via `ListByThreadAsync` into `Messages` as already-`IsComplete` entries; on not-found, sets `IsNotFound = true` and clears `Messages`/`ThreadId`), and `ResetToNewAsync()` (clears `ThreadId`/`Messages`/`ComposerText`/`ErrorMessage`/`IsNotFound`) to `src/EnterpriseAIPlatform.Web/Services/ChatComposerState.cs` (depends on T023, T024)
- [X] T036 [P] [US3] Unit tests for `ChatComposerState.SwitchToAsync`/`ResetToNewAsync` (existing thread loads its history; foreign/nonexistent thread sets `IsNotFound`; a subsequent successful switch clears `IsNotFound`) in `tests/EnterpriseAIPlatform.UnitTests/Web/ChatComposerStateTests.cs` (depends on T035)
- [X] T037 [US3] Create `ConversationListState` (Scoped: `Conversations` (`List<ChatThreadModel>`), `IsLoading`, `RenameErrorMessage`, `LoadConversationsAsync()`, `RenameAsync(threadId, newName)`) in `src/EnterpriseAIPlatform.Web/Services/ConversationListState.cs` (depends on T023)
- [X] T038 [P] [US3] Unit tests for `ConversationListState` (`LoadConversationsAsync` populates `Conversations`; successful `RenameAsync` updates the matching entry's `DisplayName`; empty/whitespace `RenameAsync` sets `RenameErrorMessage` and leaves the entry unchanged) in `tests/EnterpriseAIPlatform.UnitTests/Web/ConversationListStateTests.cs` (depends on T037)
- [X] T039 [US3] Register `ConversationListState` as a `Scoped` service in `src/EnterpriseAIPlatform.Web/Program.cs` (depends on T037)

### Web/Blazor UI

- [X] T040 [P] [US3] Create `ConversationList.razor` (sidebar list bound to `ConversationListState.Conversations`; an empty-state invitation when the list is empty; inline-edit rename — click a name to make it an editable field, Enter/blur calls `RenameAsync`, Escape discards) + `ConversationList.razor.css` in `src/EnterpriseAIPlatform.Web/Components/Chat/`
- [X] T041 [US3] Create `ChatShell.razor` (takes an optional `ThreadId` parameter; in `OnParametersSetAsync`, calls `ChatComposerState.SwitchToAsync(ThreadId)` if set, else `ResetToNewAsync()`; renders `<ConversationList>` alongside either the active `<ChatTranscript>`/`<ChatComposer>` or a not-found block when `ChatComposerState.IsNotFound`) + `ChatShell.razor.css` in `src/EnterpriseAIPlatform.Web/Components/Chat/ChatShell.razor` (depends on T035, T040)
- [X] T042 [US3] Create `ChatConversation.razor` — a thin `@page "/chat/{ThreadId}"` host rendering `<ChatShell ThreadId="ThreadId" />`, mirroring the existing `Home.razor`→`ChatHome.razor` thin-page pattern — in `src/EnterpriseAIPlatform.Web/Components/Pages/ChatConversation.razor` (depends on T041)
- [X] T043 [US3] Update `src/EnterpriseAIPlatform.Web/Components/Pages/Home.razor` to render `<ChatShell />` (no `ThreadId`) in place of `<ChatHome />`, preserving Story 1's always-blank-composer contract (depends on T041)
- [X] T044 [P] [US3] bUnit tests: `ConversationList` renders its empty state with zero conversations, renders each conversation's name, and completes an inline-rename round trip; `ChatShell` renders its not-found block when `IsNotFound` is true — in `tests/EnterpriseAIPlatform.UnitTests/Web/ConversationListComponentTests.cs` (depends on T040, T041)

**Checkpoint**: User Story 3 is independently testable — the conversation list, switch, and rename flows all work against the full quickstart.md Story 3 section, without altering Stories 1–2's behavior.

---

## Phase 7: Polish (Story 3)

- [X] T045 [US3] Run `quickstart.md`'s Story 3 validation steps end-to-end against a local `dotnet run` (Development auth mode), and re-run the full existing test suite (`UnitTests`, `IntegrationTests`, `ArchitectureTests`) to confirm no regressions in Stories 1–2

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup (T001, so `ChatComposerStateTests.cs`/bUnit-backed tests can later compile) — blocks both stories.
- **User Story 1 (Phase 3)**: Depends on Foundational. Blocks User Story 2 (Story 2 sends from the screen Story 1 builds).
- **User Story 2 (Phase 4)**: Depends on User Story 1's `Home.razor`/`ChatComposer.razor`/`ChatTranscript.razor` existing.
- **Polish (Phase 5)**: Depends on both stories complete.
- **User Story 3 (Phase 6)**: Depends on Phases 1–5 complete (`ChatComposerState`, `ChatComposer.razor`, `ChatTranscript.razor` must already exist). Internally layered: Domain (T020) → Application (T021–T024) → Infrastructure (T025–T031) → test fakes (T032) → HTTP endpoints (T033–T034) and Web state (T035–T039) in parallel → Web UI (T040–T044).
- **Polish (Phase 7)**: Depends on Phase 6 complete.

### Parallel Opportunities

- T002 can run alongside T001 (different files).
- T007 and T008 can run in parallel once T006 is done (different files).
- T011 and T012 can run in parallel (different test files); both can be written before T013–T016 land, per TDD convention, and should fail until those land.
- T017 and T018 can run in parallel once T016 is done (different files).
- T021 can run alongside T020 (different files); T022 follows T021.
- Once T023/T024 land: T032 (fakes), T035 (`ChatComposerState`), and T037 (`ConversationListState`) can all start in parallel (different files, all depending only on the Application-layer interfaces).
- T033 (endpoints) and T034 (integration tests) depend on T032, but T035–T039 (Web state) don't depend on T033/T034 at all — the Web and HTTP tracks can proceed in parallel once the Application/Infrastructure layers (T020–T031) are done.
- T040 and T041 touch different files but T041 depends on T040 existing as a child component reference; T042 and T043 can run in parallel once T041 is done (different files).

---

## Parallel Example: User Story 2

```bash
# Once T016 (streaming consumption) is done:
Task: "Wire ChatComposer.razor's submit action to ChatComposerState.SendAsync"
Task: "Update ChatTranscript.razor to distinguish in-progress/interrupted messages"
```

## Parallel Example: User Story 3

```bash
# Once T023/T024 (Application-layer interfaces) are done:
Task: "Add list/rename/touch methods to FakeChatThreadStore and FakeChatMessageStore"
Task: "Add SwitchToAsync/ResetToNewAsync/IsNotFound to ChatComposerState"
Task: "Create ConversationListState"

# Once T041 (ChatShell) is done:
Task: "Create ChatConversation.razor hosting <ChatShell ThreadId=... />"
Task: "Update Home.razor to host <ChatShell /> with no ThreadId"
```

---

## Implementation Notes (discovered during build)

- **Global render mode**: the app had no component anywhere declaring an interactive render mode (only `Program.cs`'s `AddInteractiveServerRenderMode()`, which just makes it *available*). Fixed by adding `@rendermode="InteractiveServer"` to `<Routes />` in `App.razor` — the idiomatic "Global Server" interactivity location — rather than per-page, so this and future pages get interactivity for free.
- **`ChatHome.razor` extraction**: `Home.razor` (the `@page "/"` component) is a thin host for a new non-routed `ChatHome.razor` (identity header + transcript + composer) under `Components/Chat/`. This was necessary because bUnit 2.9.0 cannot mount an `@page`-annotated component directly (`Render<T>` reports `ComponentNotFoundException` regardless of content — confirmed with a minimal repro); it's also better practice generally (thin page, testable content component).
- **bUnit 2.9.0 API**: the installed version renamed `TestContext`→`BunitContext` and `RenderComponent`→`Render`, and `Render<T>(Action<ComponentParameterCollectionBuilder<T>>)` does not mount a component when zero parameters are added — worked around by using the `Render(RenderFragment)` overload with an explicit `OpenComponent`/`CloseComponent` pair for `ChatHome`.
- **Deterministic streaming test**: the original "IsStreaming is true mid-flight" test design (checking state right after an un-awaited `SendAsync()` call against a `Task.Yield()`-based fake stream) was racy — the yield's continuation can run on a thread-pool thread before the assertion executes. Replaced with a `TaskCompletionSource`-gated fake stream so the mid-flight state is checked deterministically.

### Story 3 (T020–T045)

- **`bUnit` no-parameter-component bug applies to strongly-typed render too**: `Render<T>(Action<ComponentParameterCollectionBuilder<T>>)` fails to mount `ConversationList` (which takes no `[Parameter]`s) the same way it failed for `ChatHome` in Stories 1–2. Fix: use `Render<T>(RenderFragment)` (equivalent to `Render(fragment).FindComponent<T>()`) — this returns the strongly-typed `IRenderedComponent<T>` needed for `WaitForState`, unlike the plain `Render(RenderFragment)` overload used for one-off `ChatHome` renders.
- **List-loading ownership moved from `ChatShell` to `ConversationList`**: the plan/tasks described `ChatShell.OnInitializedAsync` calling `ListState.LoadConversationsAsync()`. In practice this made `ConversationList` untestable in isolation (nothing triggered its own data load when bUnit-rendered directly) and coupled `ChatShell` to a concern it doesn't otherwise touch. Moved the call into `ConversationList.OnInitializedAsync()` instead — self-contained, and `ChatShell` no longer needs `ConversationListState` injected at all.
- **`ResetToNewAsync` renamed to `ResetToNew`**: the method has no I/O (just clears in-memory state), so it's synchronous — named without the `Async` suffix per normal .NET convention rather than matching the docs' original (async-suffixed but non-async) name.
- **Graceful degradation added, not in the original task list**: a live smoke test (T045) surfaced that a real Cosmos-backed run 500's the *entire* `/` page if `IChatThreadStore.ListByOwnerAsync` throws (e.g., Cosmos unreachable) — previously Stories 1–2 never touched Cosmos until the first send, so this was a new failure mode. Fixed by wrapping `ConversationListState.LoadConversationsAsync` in a try/catch that sets a new `LoadErrorMessage` instead of propagating: the sidebar shows a clear degraded-state message while the identity header and composer keep working normally. Also required updating `DevelopmentAuthenticationTests`'s `WebApplicationFactory` (which renders `/` end-to-end without Cosmos) to swap in `FakeChatThreadStore`, since it hit exactly this failure mode as a test regression before the fix.

## Implementation Strategy

### MVP First

1. Phase 1 (Setup) → Phase 2 (Foundational) → Phase 3 (User Story 1).
2. **Stop and validate**: confirm the landing screen is real and auth-gated (T005, T010).
3. This alone is demoable — "here's a real screen, not a placeholder" — even before Story 2 lands.

### Incremental Delivery

1. Setup + Foundational → Story 1 → validate → Story 2 → validate with `quickstart.md` (T019). **Complete.**
2. Story 3 (this pass): Domain/Application/Infrastructure layers (T020–T031) → test fakes (T032) → HTTP endpoints + Web state in parallel (T033–T039) → Web UI (T040–T044) → validate with `quickstart.md`'s Story 3 section + full regression suite (T045).
3. Stories 4–5 (multi-pane compare, changelog) remain a separate, later `/speckit-tasks` pass against the same plan.md's follow-on phases.
