# Tasks: Multi-Chat Session Persistence (R1 subset)

**Input**: Design documents from `/specs/006-multi-chat-session-persistence/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)
**Tests**: Included — this spec's R1-scoped success criteria (SC-001, SC-002, SC-003, SC-005) are defined as test suites, and Constitution Principle VI requires falsifiable per-story tests.

**Release 1 scope note** (per `plan.md`'s Summary, confirmed by explicit user decision to build spec 006 as written): this tasks.md covers **US1, US2, US4 (all P1)**. **Explicitly deferred to R2 — no tasks generated here, not forgotten**:
- **US3** (Chat-Home starred personas, P3) — no data-loss/reliability risk; personas (specs 009/010) aren't in R1, so there's nothing to star yet.
- **Per-quadrant persona assignment** — the `PersonaId` field exists in the schema (T004) but is never set/read functionally in R1; only `ModelId` assignment is wired up (plan.md Summary).

## Implementation status (2026-07-29)

All 23 R1 tasks complete. `dotnet build` clean; **138/138 tests pass** across the whole solution (14 architecture, 86 unit, 38 integration — up from specs 002+004+014's 119, +19 for this spec). No live Azure/Cosmos dependency required — `IMultiChatSessionStore`/`IChatThreadStore`/`IChatCompletionClient` are all swapped for in-memory fakes in tests, reusing spec 004's `ChatWebApplicationFactory` fixture extended with a fake session store.

The quadrant floor/cap invariant (FR-004/005) was extracted into a pure `MultiChatQuadrantRules` static class (Application layer, no I/O) rather than embedded directly in `CosmosMultiChatSessionStore` — mirrors spec 014's `ModelAccessEvaluator` pattern and lets both the real Cosmos store and the test fake share one rule implementation, so they can't drift apart (Principle IV).

## Path Conventions (from plan.md — extends specs 002/004/014's layered solution, no new project)

- `src/EnterpriseAIPlatform.Domain/Chat/`, `.Application/Chat/`, `.Infrastructure/Chat/`, `.Web/Endpoints/MultiChat/`
- `tests/EnterpriseAIPlatform.UnitTests/`, `.IntegrationTests/`, `.ArchitectureTests/`

---

## Phase 1: Setup

- [X] T001 Confirm no new NuGet dependencies are required (`System.Threading.Channels` is part of the BCL) and no new configuration is needed (reuses spec 004's `Cosmos:ChatContainerName`)

**Checkpoint**: Nothing to restore; solution still builds.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ No user story work begins until this phase is complete.**

- [X] T002 [P] Add nullable `MultiChatSessionId`/`MultiChatPosition` fields to spec 004's `ChatThreadModel` in `src/EnterpriseAIPlatform.Domain/Chat/ChatThreadModel.cs`
- [X] T003 [P] Extend `IChatThreadStore.CreateAsync` with optional `multiChatSessionId`/`multiChatPosition` parameters (default null — existing spec 004 call sites unaffected); update `ChatThreadDocument` mapping and `CosmosChatThreadStore` in `src/EnterpriseAIPlatform.Application/Chat/IChatThreadStore.cs` and `src/EnterpriseAIPlatform.Infrastructure/Chat/`
- [X] T004 [P] Create `MultiChatSession`, `MultiChatQuadrant`, `QuadrantEvent` types in `src/EnterpriseAIPlatform.Domain/Chat/`
- [X] T005 Declare `IMultiChatSessionStore` contract in `src/EnterpriseAIPlatform.Application/Chat/`
- [X] T006 Implement `CosmosMultiChatSessionStore` (get-or-create; add/remove enforcing the 2–4 invariant; assign-model; set-quadrant-thread) in `src/EnterpriseAIPlatform.Infrastructure/Chat/`
- [X] T007 Register `IMultiChatSessionStore` in DI in `src/EnterpriseAIPlatform.Infrastructure/DependencyInjection/ChatServiceCollectionExtensions.cs`

**Checkpoint**: Session store is unit-testable via EF/Cosmos-free fakes; `ChatThreadModel` extension doesn't break any existing spec 004 test.

---

## Phase 3: User Story 1 — Multi-chat layout survives a page refresh, part 1: session/quadrant CRUD (Priority: P1) 🎯 MVP

**Goal**: Quadrant count, model assignment, and thread association are durably persisted and restored on load; count always stays in [2, 4].
**Independent Test**: Create a session, mutate quadrant count/assignments, fetch again, confirm identical state (SC-001); attempt to exceed the cap or go below the floor and confirm the server-enforced bound holds.

- [X] T008 [P] [US1] Integration test: `GET /api/multichat/session` creates a default 2-quadrant session; assign a model; `GET` again; assert identical state (SC-001) in `tests/EnterpriseAIPlatform.IntegrationTests/`
- [X] T009 [P] [US1] Unit tests: adding past the 4-quadrant cap is rejected (count stays 4); removing past the 2-quadrant floor clears the last quadrant's assignment instead of dropping the count (FR-004/005) in `tests/EnterpriseAIPlatform.UnitTests/`
- [X] T010 [US1] Implement `GET /api/multichat/session` (get-or-create) in `src/EnterpriseAIPlatform.Web/Endpoints/MultiChat/`
- [X] T011 [US1] Implement `POST`/`DELETE /api/multichat/session/quadrants` (add/remove) in `src/EnterpriseAIPlatform.Web/Endpoints/MultiChat/`
- [X] T012 [US1] Implement `PUT /api/multichat/session/quadrants/{position}/model` (validates the model exists/is enabled via spec 014's `IModelCatalogService` before persisting) in `src/EnterpriseAIPlatform.Web/Endpoints/MultiChat/`

**Checkpoint**: Session/quadrant CRUD fully functional and independently testable (MVP slice — no send yet).

---

## Phase 4: User Story 4 — Send one message to multiple models in parallel (Priority: P1)

**Goal**: A single message is dispatched concurrently to every quadrant's assigned model; each quadrant's response streams independently, unblocked by another quadrant's latency or failure.
**Independent Test**: Assign distinct models to 2–4 quadrants; send one message; confirm all quadrants' responses arrive tagged and interleaved in one stream (SC-005).

- [X] T013 [P] [US4] Unit test: the fan-in merge writes `Chunk`/`Done` events from N concurrently-running quadrant tasks without one waiting for another to complete (using fakes with staggered delays) in `tests/EnterpriseAIPlatform.UnitTests/`
- [X] T014 [P] [US4] Integration test: `POST /api/multichat/session/messages` with 2+ quadrants assigned distinct models (via spec 004's fake `IChatCompletionClient`) returns a `text/event-stream` containing every quadrant's tagged `chunk`/`done` events (SC-005) in `tests/EnterpriseAIPlatform.IntegrationTests/`
- [X] T015 [US4] Implement the `Channel<QuadrantEvent>`-based fan-out/fan-in dispatcher (one concurrent task per assigned quadrant, each calling spec 004's `IChatPipeline.SendMessageAsync`) in `src/EnterpriseAIPlatform.Infrastructure/Chat/`
- [X] T016 [US4] Implement `POST /api/multichat/session/messages`, streaming the dispatcher's tagged events as SSE, in `src/EnterpriseAIPlatform.Web/Endpoints/MultiChat/`

**Checkpoint**: US1 + US4 together deliver the full "author once, compare side-by-side" vertical.

---

## Phase 5: User Story 1 — Multi-chat layout survives a page refresh, part 2: on-demand thread creation persists immediately (Priority: P1)

**Goal**: The first send in an empty quadrant creates a thread and persists that association before the send proceeds, so an immediate refresh doesn't lose it.
**Independent Test**: Send to an empty quadrant, then fetch the session again before/immediately after — the new `ThreadId` is already present.

- [X] T017 [P] [US1] Integration test: send to a quadrant with no `ThreadId`; immediately `GET /api/multichat/session`; assert the new `ThreadId` is already persisted (extends SC-001) in `tests/EnterpriseAIPlatform.IntegrationTests/`
- [X] T018 [US1] Wire on-demand `IChatThreadStore.CreateAsync` (passing `multiChatSessionId`/`multiChatPosition`, T003) + `IMultiChatSessionStore.SetQuadrantThreadAsync` into the dispatcher, executed and persisted *before* dispatching that quadrant to `IChatPipeline` (FR-003) in `src/EnterpriseAIPlatform.Infrastructure/Chat/`

**Checkpoint**: FR-003's "survives an immediate refresh" guarantee is now real, not just schema-ready.

---

## Phase 6: User Story 2 — Failed sends surface an error and never lose the user's input (Priority: P1, R1 backend contract)

**Goal**: A thread-creation failure for one quadrant during the parallel send never faults the whole request and is never silently swallowed.
**Independent Test**: Force one quadrant's on-demand thread creation to fail while others succeed; confirm the overall response is still `200`, the failing quadrant emits an `error` event, and the others still stream normally.

- [X] T019 [P] [US2] Unit/integration test: one quadrant's thread-creation (fake) throws mid-dispatch; assert its channel event is `Kind=Error`, the response status is still `200` (not 500), and other quadrants' `chunk`/`done` events are unaffected (SC-002/003) in `tests/EnterpriseAIPlatform.IntegrationTests/`
- [X] T020 [US2] Catch per-quadrant exceptions in the dispatcher's per-quadrant task and write a `QuadrantEvent { Kind = Error }` instead of letting the exception propagate/fault the shared channel, in `src/EnterpriseAIPlatform.Infrastructure/Chat/`

**Checkpoint**: All R1-scoped stories (US1, US2, US4) independently functional and integrated into one working endpoint.

---

## Phase 7: Polish & Cross-Cutting

- [X] T021 [P] Architecture test asserting exactly one `IMultiChatSessionStore` implementation in `tests/EnterpriseAIPlatform.ArchitectureTests/`
- [X] T022 [P] Update the solution README with spec 006's endpoints and the explicit R1 (US1/US2/US4, model-assignment only) vs. R2 (US3, persona-assignment) scope note
- [X] T023 Verify SC-001, SC-002, SC-003, SC-005 are each covered by a passing test; confirm SC-004 (US3) is explicitly recorded as deferred to R2, not silently skipped

**SC coverage map (verified 2026-07-29, 138/138 tests passing across the solution):**
| SC | Covered by |
|---|---|
| SC-001 | `MultiChatSessionTests.GetSession_CreatesDefault_WithTwoQuadrants`, `.AssignModel_ThenGetAgain_ReturnsIdenticalState`, `.AddQuadrant_PastCap_IsRejected`, `.RemoveQuadrant_AtFloor_ClearsAssignment_NeverDropsBelowTwo`, `.ParallelSend_ToEmptyQuadrant_PersistsThreadImmediately`; `MultiChatQuadrantRulesTests` (unit-level floor/cap) |
| SC-002 | `MultiChatSessionTests.ParallelSend_OneQuadrantFailsToCreateThread_OthersStillSucceed`; `MultiChatDispatcherTests.DispatchAsync_OneQuadrantThrows_OthersStillCompleteNormally` |
| SC-003 | Same as SC-002 — the failing quadrant's `error` event is a visible, structured signal, never console-only |
| SC-004 | **Deferred to R2** — Chat-Home (US3) depends on personas (specs 009/010), not in R1 |
| SC-005 | `MultiChatSessionTests.ParallelSend_DispatchesToAllAssignedQuadrants_TaggedByPosition`; `MultiChatDispatcherTests.DispatchAsync_MergesChunksFromMultipleQuadrants_EachTaggedWithItsPosition` |

---

## Dependencies & Execution Order

- **Setup** → **Foundational** block everything.
- **US1 session/quadrant CRUD** (Phase 3) depends only on Foundational.
- **US4 parallel dispatch** (Phase 4) depends on Foundational (needs `IMultiChatSessionStore` to read quadrant assignments) but not on Phase 3's endpoints existing — both can proceed in parallel once Foundational lands.
- **US1's on-demand creation** (Phase 5) depends on Phase 4's dispatcher existing (it's wired *into* the dispatcher).
- **US2's error isolation** (Phase 6) depends on Phase 5 (there must be a real on-demand-creation call site to make fail).
- Polish last.
- **Deferred to R2, not sequenced here**: US3 (depends on personas, specs 009/010, not in R1); persona-assignment (same dependency).

**Parallel opportunities**: T002/T003/T004 (Foundational); all `[P]` test tasks within a phase; Phase 3 (session CRUD) and the test-writing halves of Phase 4 can proceed in parallel once Foundational lands.

## Implementation Strategy

Deliver **US1's session/quadrant CRUD first** (Phase 3 — the cheapest, most independently testable slice), then **US4's parallel-dispatch core** (Phase 4 — the vertical's centerpiece), then layer in **US1's on-demand-creation timing guarantee** (Phase 5) and **US2's error isolation** (Phase 6) on top of the same dispatcher. Keep the architecture test (T021) green throughout. Do not begin US3 or persona-assignment work against this tasks.md — that's explicitly R2 scope requiring its own `/speckit-tasks` pass once personas (009/010) exist.
