# Tasks: Chat Web UI — Stories 1–2 slice

**Input**: Design documents from `/specs/024-chat-web-ui/` (plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md)

**Scope**: Only User Story 1 (sign-in/chat home) and User Story 2 (start conversation, streaming response), per plan.md's scope note. Stories 3–5 (list/rename, multi-pane compare, changelog) are deferred to a later tasks pass.

**Tests**: Included — plan.md's Testing section and Project Structure explicitly call out `ChatComposerStateTests.cs`, `ChatComposerComponentTests.cs`, and `ChatHomeAuthTests.cs` as part of this feature's deliverables.

**Organization**: Tasks are grouped by user story. Both stories in this pass are Priority P1 per spec.md; Story 1 is a hard prerequisite for Story 2 (nothing in Story 2 is reachable without Story 1's screen existing), so they are sequenced rather than parallelized across stories.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no unmet dependency)
- **[Story]**: US1 or US2, per spec.md

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

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup (T001, so `ChatComposerStateTests.cs`/bUnit-backed tests can later compile) — blocks both stories.
- **User Story 1 (Phase 3)**: Depends on Foundational. Blocks User Story 2 (Story 2 sends from the screen Story 1 builds).
- **User Story 2 (Phase 4)**: Depends on User Story 1's `Home.razor`/`ChatComposer.razor`/`ChatTranscript.razor` existing.
- **Polish (Phase 5)**: Depends on both stories complete.

### Parallel Opportunities

- T002 can run alongside T001 (different files).
- T007 and T008 can run in parallel once T006 is done (different files).
- T011 and T012 can run in parallel (different test files); both can be written before T013–T016 land, per TDD convention, and should fail until those land.
- T017 and T018 can run in parallel once T016 is done (different files).

---

## Parallel Example: User Story 2

```bash
# Once T016 (streaming consumption) is done:
Task: "Wire ChatComposer.razor's submit action to ChatComposerState.SendAsync"
Task: "Update ChatTranscript.razor to distinguish in-progress/interrupted messages"
```

---

## Implementation Notes (discovered during build)

- **Global render mode**: the app had no component anywhere declaring an interactive render mode (only `Program.cs`'s `AddInteractiveServerRenderMode()`, which just makes it *available*). Fixed by adding `@rendermode="InteractiveServer"` to `<Routes />` in `App.razor` — the idiomatic "Global Server" interactivity location — rather than per-page, so this and future pages get interactivity for free.
- **`ChatHome.razor` extraction**: `Home.razor` (the `@page "/"` component) is a thin host for a new non-routed `ChatHome.razor` (identity header + transcript + composer) under `Components/Chat/`. This was necessary because bUnit 2.9.0 cannot mount an `@page`-annotated component directly (`Render<T>` reports `ComponentNotFoundException` regardless of content — confirmed with a minimal repro); it's also better practice generally (thin page, testable content component).
- **bUnit 2.9.0 API**: the installed version renamed `TestContext`→`BunitContext` and `RenderComponent`→`Render`, and `Render<T>(Action<ComponentParameterCollectionBuilder<T>>)` does not mount a component when zero parameters are added — worked around by using the `Render(RenderFragment)` overload with an explicit `OpenComponent`/`CloseComponent` pair for `ChatHome`.
- **Deterministic streaming test**: the original "IsStreaming is true mid-flight" test design (checking state right after an un-awaited `SendAsync()` call against a `Task.Yield()`-based fake stream) was racy — the yield's continuation can run on a thread-pool thread before the assertion executes. Replaced with a `TaskCompletionSource`-gated fake stream so the mid-flight state is checked deterministically.

## Implementation Strategy

### MVP First

1. Phase 1 (Setup) → Phase 2 (Foundational) → Phase 3 (User Story 1).
2. **Stop and validate**: confirm the landing screen is real and auth-gated (T005, T010).
3. This alone is demoable — "here's a real screen, not a placeholder" — even before Story 2 lands.

### Incremental Delivery

1. Setup + Foundational → Story 1 → validate → Story 2 → validate with `quickstart.md` (T019).
2. Stories 3–5 (list/rename, multi-pane compare, changelog) are a separate, later `/speckit-tasks` pass against the same plan.md's follow-on phases.
