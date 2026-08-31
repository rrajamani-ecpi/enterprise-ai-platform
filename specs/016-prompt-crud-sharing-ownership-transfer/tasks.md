# Tasks: Prompt CRUD, Sharing & Ownership Transfer

**Input**: Design documents from `/specs/016-prompt-crud-sharing-ownership-transfer/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: INCLUDED. The spec's Success Criteria (SC-001–SC-008) are each mapped to a named test in [quickstart.md](./quickstart.md), and Constitution Principle VI requires testable EARS-style requirements. Test tasks are written before the implementation they cover within each phase.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US6 per spec.md)
- Include exact file paths in descriptions

## Path Conventions

Layered monolith (see plan.md → Project Structure). No new projects; `Prompts` folders are peers to `Personas`/`ModelAccess`/`Chat`/`Sharing`:

- `src/EnterpriseAIPlatform.Domain/`, `.Application/`, `.Infrastructure/`, `.Web/`
- `tests/EnterpriseAIPlatform.UnitTests/`, `.IntegrationTests/`, `.ArchitectureTests/`

## Story Ordering Note

Spec priorities are US1 (P1), US2 (P1), US3 (P2), US4 (P1), US5 (P2), US6 (P2). Within the P1 band **US4 is sequenced first**: the spec itself states "Basic CRUD is the foundation every other story in this spec depends on," and US1/US2 transfer semantics are unverifiable without a prompt to transfer. US4 is therefore the MVP increment, followed by US1 and US2 to complete the P1 band.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish a clean baseline and the folder skeleton before any code is written

- [ ] T001 Capture a green baseline by running `dotnet build EnterpriseAIPlatform.slnx` and `dotnet test EnterpriseAIPlatform.slnx`, recording the passing test count — this feature edits types owned by specs 002/014/024, so a pre-change baseline is required to prove no prior test regressed
- [ ] T002 [P] Create the feature folder skeleton: `src/EnterpriseAIPlatform.Domain/Prompts/`, `src/EnterpriseAIPlatform.Application/Prompts/`, `src/EnterpriseAIPlatform.Infrastructure/Prompts/`, `src/EnterpriseAIPlatform.Web/Endpoints/Prompts/`, `src/EnterpriseAIPlatform.Web/Components/Prompts/`, `tests/EnterpriseAIPlatform.UnitTests/Prompts/`, `tests/EnterpriseAIPlatform.IntegrationTests/Prompts/`
- [ ] T003 [P] Add the `PromptSql:ConnectionString` configuration key placeholder in `src/EnterpriseAIPlatform.Web/appsettings.Development.json`, mirroring how `PersonaSql`/`ModelAccessSql` are declared by specs 009/014

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Entities, the access gate, persistence, and test scaffolding that every user story depends on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Domain entities

- [ ] T004 [P] Create `PromptShareTarget` in `src/EnterpriseAIPlatform.Domain/Prompts/PromptShareTarget.cs` with `Type`/`Identity`/`GroupToken` and `ForIndividual`/`ForGroup` factory helpers, reusing spec 018's existing `ShareTargetType` enum rather than declaring a parallel one (Principle IV)
- [ ] T005 [P] Create `PromptModel` in `src/EnterpriseAIPlatform.Domain/Prompts/PromptModel.cs` with `Id`, `OwnerUserId`, `OwnerPartitionKey`, `Name`, `Description`, `CollaboratorPartitionKeys`, `SharedWith`, `IsPublished`, `RowVersion` (Guid), `CreatedAtUtc`, `UpdatedAtUtc` per data-model.md
- [ ] T006 [P] Create `PromptFavorite` in `src/EnterpriseAIPlatform.Domain/Prompts/PromptFavorite.cs` with `UserPartitionKey`, `PromptId`, `FavoritedAtUtc` per data-model.md
- [ ] T007 [P] Create the request/response DTOs in `src/EnterpriseAIPlatform.Domain/Prompts/PromptContracts.cs`: `PromptPublicDTO`, `CreatePromptRequest`, `UpdatePromptRequest`, and `PromptOperation` — deliberately excluding the transfer DTO, which is defined in US1 (T032)

### Cross-spec prerequisite: group tokens (research.md D6)

- [ ] T008 Add `GroupTokens` (`IReadOnlyList<string>`, defaulting to empty) to `src/EnterpriseAIPlatform.Application/Identity/UserModel.cs` — additive with a safe default so every existing construction site compiles unchanged
- [ ] T009 Populate `GroupTokens` from the Entra `groups` claim in `src/EnterpriseAIPlatform.Infrastructure/Authentication/RoleClaimsTransformation.cs`, retaining the GUIDs it currently reads and discards after deriving role flags (depends on T008)
- [ ] T010 Add `GroupTokens` coverage in `tests/EnterpriseAIPlatform.UnitTests/Authentication/RoleClaimsTransformationTests.cs` and re-run spec 002's existing identity tests to confirm they pass **unmodified** — if any prior test required editing, T008/T009 were not additive and must be revisited (depends on T009)

### Access gate & validation (Application layer, DI-free)

- [ ] T011 [P] Create `PromptValidationRules` in `src/EnterpriseAIPlatform.Application/Prompts/PromptValidationRules.cs` with `TryValidate(PromptModel, out string error)` enforcing non-empty/non-whitespace `Name` and `Description` (FR-003/FR-013/FR-014)
- [ ] T012 Create `PromptAccessEvaluator` in `src/EnterpriseAIPlatform.Application/Prompts/PromptAccessEvaluator.cs` as a static class with `CanRead`/`CanWrite`/`CanTransfer` over `(PromptModel, UserModel, string callerPartitionKey)` per contracts/service-interfaces.md — write for admin/owner/collaborator, read additionally for share targets, transfer for owner/admin only (depends on T005, T008)
- [ ] T013 [P] Write `PromptAccessEvaluatorTests` in `tests/EnterpriseAIPlatform.UnitTests/Prompts/PromptAccessEvaluatorTests.cs` covering the full {owner, admin, collaborator, individual-share, group-share, unrelated} × {read, write, transfer} matrix — SC-004 (depends on T012)
- [ ] T014 [P] Write `PromptValidationRulesTests` in `tests/EnterpriseAIPlatform.UnitTests/Prompts/PromptValidationRulesTests.cs` covering empty, whitespace-only, and valid `Name`/`Description` (depends on T011)
- [ ] T015 [P] Define `IPromptService` in `src/EnterpriseAIPlatform.Application/Prompts/IPromptService.cs` with the full member list from contracts/service-interfaces.md, all returning `ServerActionResponse<T>`

### Persistence & wiring

- [ ] T016 Create `PromptDbContext` in `src/EnterpriseAIPlatform.Infrastructure/Prompts/PromptDbContext.cs` — JSON `ValueConverter` **paired with a `ValueComparer`** for `CollaboratorPartitionKeys`/`SharedWith`, `RowVersion` as `IsConcurrencyToken()` and **not** `IsRowVersion()` (research.md D10), `PromptFavorite` composite key `(UserPartitionKey, PromptId)` with an FK to `PromptModel` using `OnDelete(DeleteBehavior.Cascade)` (depends on T005, T006)
- [ ] T017 Generate the initial EF Core migration into `src/EnterpriseAIPlatform.Infrastructure/Prompts/Migrations/` via `dotnet ef migrations add InitialPromptSchema --context PromptDbContext`, and verify the generated SQL includes the favorites cascade (depends on T016)
- [ ] T018 Create `PromptServiceCollectionExtensions.AddPromptInfrastructure(...)` in `src/EnterpriseAIPlatform.Infrastructure/DependencyInjection/PromptServiceCollectionExtensions.cs` using a lazy connection string so the app boots without a live SQL dependency, and call it from `src/EnterpriseAIPlatform.Web/Program.cs` (depends on T016)
- [ ] T019 Create `PromptWebApplicationFactory` in `tests/EnterpriseAIPlatform.IntegrationTests/PromptWebApplicationFactory.cs` by copying `PersonaWebApplicationFactory`'s pattern exactly — remove **only** `PromptDbContext`'s own service descriptors (a blanket EF-namespace sweep strips sibling contexts and breaks `ValidateOnBuild`) and re-register with its own `UseInternalServiceProvider(...)` to avoid the "multiple database providers registered" failure (research.md D9) (depends on T018)
- [ ] T020 Create `PromptEndpoints` in `src/EnterpriseAIPlatform.Web/Endpoints/Prompts/PromptEndpoints.cs` with a `MapPromptEndpoints` extension and a private `ToHttpResult` mapper (OK→200, UNAUTHORIZED→401 JSON, concurrency-conflict message→409, else→400 — deliberately with **no** NOT_FOUND branch, per FR-009), and register it in `Program.cs` (depends on T018)

**Checkpoint**: Foundation ready — entities, access gate, persistence, and test host all exist. User story implementation can begin.

---

## Phase 3: User Story 4 - Create, edit, and delete reusable prompt templates (Priority: P1) 🎯 MVP

**Goal**: Full authorized CRUD over prompt templates, with share targets validated through spec 018's policy service.

**Independent Test**: Create a prompt with a name and description, confirm it appears in the prompt library, edit its description and confirm the change persists, then delete it and confirm it no longer appears.

### Tests for User Story 4

- [ ] T021 [P] [US4] Write `PromptCrudTests` in `tests/EnterpriseAIPlatform.IntegrationTests/Prompts/PromptCrudTests.cs` — create → read → update → read → delete → read, run as owner, as admin, and as collaborator; plus FR-003 rejection of empty `name`/`description` — SC-005
- [ ] T022 [P] [US4] Write `PromptWriteAuthorizationTests` in `tests/EnterpriseAIPlatform.IntegrationTests/Prompts/PromptWriteAuthorizationTests.cs` asserting share-target and unrelated callers receive 401 on `PATCH`/`DELETE` and that the stored row is unchanged afterwards — SC-004
- [ ] T023 [P] [US4] Write `PromptEnumerationTests` in `tests/EnterpriseAIPlatform.IntegrationTests/Prompts/PromptEnumerationTests.cs` asserting that a forbidden-but-existing prompt id and a never-existing prompt id produce byte-identical 401 responses (status, body, headers) — FR-009

### Implementation for User Story 4

- [ ] T024 [US4] Implement `PromptService` CRUD in `src/EnterpriseAIPlatform.Infrastructure/Prompts/PromptService.cs` — `ListAsync`, `GetAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync` — gating every path through `PromptAccessEvaluator`, server-deriving owner fields from `ICurrentUserAccessor`, and defining the shared `UnauthorizedMessage` and `ConcurrencyConflictMessage` constants (depends on T012, T015, T016)
- [ ] T025 [US4] Enforce FR-009 in `PromptService` by collapsing the missing-row and access-denied branches into a single `Unauthorized(UnauthorizedMessage)` return — never `ServerActionResponse.NotFound()` — per contracts/authorization-policies.md (depends on T024)
- [ ] T026 [US4] Validate every `SharedWith` entry on create and update through spec 018's `ISharingPolicyService`, surfacing the returned `SharingDecisionReason` in the validation error, with no prompt-local sharing rule anywhere in the feature — FR-004 (depends on T024)
- [ ] T027 [US4] Call `PromptValidationRules.TryValidate` from `CreateAsync` and `UpdateAsync` in the Application layer so a direct API caller is bound identically to a UI user — FR-003, Principle V (depends on T024)
- [ ] T028 [US4] Wire the CRUD routes `GET /api/prompts`, `GET /api/prompts/{id}`, `POST /api/prompts`, `PATCH /api/prompts/{id}`, `DELETE /api/prompts/{id}` in `src/EnterpriseAIPlatform.Web/Endpoints/Prompts/PromptEndpoints.cs` per contracts/route-table.md, applying the server-side visibility filter to the list route (depends on T020, T024)
- [ ] T029 [US4] Build the prompt library UI in `src/EnterpriseAIPlatform.Web/Components/Prompts/PromptLibrary.razor` — list, create, edit, and delete, hiding write controls for read-only callers while relying on the server gate as the actual enforcement (depends on T028)
- [ ] T030 [US4] Add a **Prompts** navigation link to `src/EnterpriseAIPlatform.Web/Components/Chat/SidebarNav.razor` alongside the existing Compare and Changelog links (depends on T029)

**Checkpoint**: Prompt CRUD is fully functional, authorized, and demoable — MVP complete.

---

## Phase 4: User Story 1 - Ownership transfer cannot be used to inject arbitrary field values (Priority: P1)

**Goal**: Transfer changes ownership and nothing else, regardless of what the request body claims.

**Independent Test**: As an authorized owner/admin, issue a transfer request whose body sets `name`, `description`, `createdAt`, and `sharedWith` to values differing from the stored record, and confirm the transferred prompt retains every original server-side value except owner.

### Tests for User Story 1

- [ ] T031 [P] [US1] Write `PromptTransferFieldInjectionTests` in `tests/EnterpriseAIPlatform.IntegrationTests/Prompts/PromptTransferFieldInjectionTests.cs` with a corpus of transfer bodies carrying forged `name`/`description`/`createdAt`/`sharedWith`/`ownerUserId`/`collaboratorPartitionKeys`, asserting after each that every non-ownership field equals its pre-transfer value, plus a case asserting a non-owner/non-admin caller is rejected with no write at all — SC-001

### Implementation for User Story 1

- [ ] T032 [US1] Define `TransferPromptOwnershipRequest` in `src/EnterpriseAIPlatform.Domain/Prompts/PromptContracts.cs` with **exactly one** property, `NewOwnerEmail` — the structural defence that makes forged fields unbindable before any handler code runs (FR-005)
- [ ] T033 [US1] Implement `TransferOwnershipAsync` in `src/EnterpriseAIPlatform.Infrastructure/Prompts/PromptService.cs`: authorize via `PromptAccessEvaluator.CanTransfer` **before any write** (FR-006), load the stored row, and mutate only `OwnerUserId`, `OwnerPartitionKey`, `UpdatedAtUtc`, and `RowVersion` — every other field re-read from the database and written back unchanged (FR-005/FR-008) (depends on T024, T032)
- [ ] T034 [US1] Wire `POST /api/prompts/{id}/transfer-ownership` in `src/EnterpriseAIPlatform.Web/Endpoints/Prompts/PromptEndpoints.cs`, authorizing in-handler rather than with a route-level `RequireAdmin` — FR-006 permits the owner **or** an admin, so a route policy would wrongly reject the owner (depends on T028, T033)
- [ ] T035 [US1] Add a transfer-ownership action to `src/EnterpriseAIPlatform.Web/Components/Prompts/PromptLibrary.razor`, visible only to the owner or an admin (depends on T029, T034)
- [ ] T036 [US1] Resolve the spec's open edge case for a transfer targeting a nonexistent or malformed recipient by rejecting the request with a validation error before any write, and add the case to `PromptTransferFieldInjectionTests` (depends on T033)

**Checkpoint**: Transfer is authorization-correct and injection-proof.

---

## Phase 5: User Story 2 - Ownership transfer is atomic and recoverable on failure (Priority: P1)

**Goal**: A failed transfer never loses or duplicates the prompt.

**Independent Test**: Trigger a transfer where the write is made to fail and confirm the original prompt still exists, fully intact, under the original owner, with the operation reporting failure.

### Tests for User Story 2

- [ ] T037 [P] [US2] Write `PromptTransferAtomicityTests` in `tests/EnterpriseAIPlatform.IntegrationTests/Prompts/PromptTransferAtomicityTests.cs` — force `SaveChangesAsync` to fail mid-transfer and assert the row still exists unchanged under the original owner, that the operation reports failure, and that an immediate retry succeeds against the intact record with no manual repair — SC-002
- [ ] T038 [P] [US2] Write `PromptConcurrencyTests` in `tests/EnterpriseAIPlatform.IntegrationTests/Prompts/PromptConcurrencyTests.cs` — two concurrent transfers of the same prompt: the first wins, the second returns HTTP 409, and the prompt exists exactly once under exactly one owner (FR-007/FR-008, double-submit edge case)

### Implementation for User Story 2

- [ ] T039 [US2] Confirm `TransferOwnershipAsync` performs a **single** `SaveChangesAsync` on one row with an immutable `Id` — no delete-then-recreate and no compensating action — and document that assertion in the method's XML summary (depends on T033)
- [ ] T040 [US2] Handle `DbUpdateConcurrencyException` in `PromptService` write and transfer paths by returning `ConcurrencyConflictMessage` rather than retrying silently or last-write-wins (Principle III), and confirm `ToHttpResult` maps it to 409 (depends on T024, T033)
- [ ] T041 [US2] Surface the 409 conflict in `src/EnterpriseAIPlatform.Web/Components/Prompts/PromptLibrary.razor` as an explicit "reload and retry" message rather than a silent failure or a generic error (depends on T035, T040)

**Checkpoint**: All P1 stories complete — transfer is secure, atomic, and concurrency-safe.

---

## Phase 6: User Story 3 - Consistent response format for prompt-generation failures (Priority: P2)

**Goal**: Total generation failure returns structured JSON matching the success path's content type.

**Independent Test**: Force both the primary and fallback models to fail and confirm the response is JSON with the same content type as the success path, carrying a structured error payload.

### Tests for User Story 3

- [ ] T042 [P] [US3] Write `PromptGenerationFailureTests` in `tests/EnterpriseAIPlatform.IntegrationTests/Prompts/PromptGenerationFailureTests.cs` covering {primary fails, no fallback} and {primary fails, fallback fails}, asserting `Content-Type: application/json` matching the success path, a structured error body, and **no** success-shaped response carrying an error string as generated content — SC-003
- [ ] T043 [P] [US3] Write `PromptGenerationFallbackTests` in `tests/EnterpriseAIPlatform.UnitTests/Prompts/PromptGenerationFallbackTests.cs` asserting the fallback is attempted **exactly once**, that a primary success never invokes the fallback, and that a model id outside `AllowedModelIds` is rejected as a configuration error rather than silently passed through — FR-010

### Implementation for User Story 3

- [ ] T044 [US3] Add `PrimaryModelId` and `FallbackModelId` to `src/EnterpriseAIPlatform.Domain/ModelAccess/PersonaGenerationModelConfig.cs`, both nullable and both required to be members of `AllowedModelIds` (research.md D8)
- [ ] T045 [US3] Generate the additive EF Core migration for spec 014's existing context via `dotnet ef migrations add AddPromptGenerationModelSelection --context ModelAccessDbContext` into `src/EnterpriseAIPlatform.Infrastructure/ModelAccess/Migrations/`, and confirm spec 014's existing tests still pass unmodified (depends on T044)
- [ ] T046 [US3] Define `IPromptGenerationService` in `src/EnterpriseAIPlatform.Application/Prompts/IPromptGenerationService.cs` per contracts/service-interfaces.md
- [ ] T047 [US3] Implement `PromptGenerationService` in `src/EnterpriseAIPlatform.Infrastructure/Prompts/PromptGenerationService.cs` — wrap user input in the existing fixed meta-prompt, call `PrimaryModelId`, fall back exactly once, and **accumulate** `IChatCompletionClient.StreamCompletionAsync` chunks into a single string rather than adding a non-streaming method to that interface (Principle IV, research.md D8) (depends on T044, T046)
- [ ] T048 [US3] Wire `POST /api/promptGenerator` in `src/EnterpriseAIPlatform.Web/Endpoints/Prompts/PromptEndpoints.cs`, returning a structured JSON error envelope on total failure — never a plain-text body and never a fabricated success (FR-011, Principle III) (depends on T020, T047)
- [ ] T049 [US3] Add a "generate with AI" affordance to `src/EnterpriseAIPlatform.Web/Components/Prompts/PromptLibrary.razor` that populates the description field and renders the structured error on failure (depends on T029, T048)

**Checkpoint**: Prompt generation succeeds and fails in a uniformly JSON-shaped way.

---

## Phase 7: User Story 5 - Favorite prompts for quick access (Priority: P2)

**Goal**: Per-user favorites, isolated between users and cleaned up structurally on delete.

**Independent Test**: With access to several prompts, favorite two, confirm both are flagged on subsequent reads, unfavorite one, and confirm only the other remains.

### Tests for User Story 5

- [ ] T050 [P] [US5] Write `PromptFavoritesTests` in `tests/EnterpriseAIPlatform.IntegrationTests/Prompts/PromptFavoritesTests.cs` — user A favorites a prompt and it appears in A's list but not B's; repeat-favorite and repeat-unfavorite are both idempotent; favoriting a prompt the caller cannot read returns 401 — SC-007, FR-016
- [ ] T051 [P] [US5] Write `PromptDeleteCascadeTests` in `tests/EnterpriseAIPlatform.IntegrationTests/Prompts/PromptDeleteCascadeTests.cs` — three users favorite a prompt, it is deleted, and it is then absent from every list, read, and **every** user's favorites, with zero `PromptFavorite` rows remaining — SC-006, FR-017
- [ ] T052 [P] [US5] Add a case to `PromptFavoritesTests` asserting that transferring ownership leaves every user's favorites untouched, including the former owner's — FR-019

### Implementation for User Story 5

- [ ] T053 [US5] Implement `ListFavoritesAsync`, `AddFavoriteAsync`, and `RemoveFavoriteAsync` in `src/EnterpriseAIPlatform.Infrastructure/Prompts/PromptService.cs`, scoping every query to the caller's own `UserPartitionKey` and checking `PromptAccessEvaluator.CanRead` before insert (FR-016) (depends on T024)
- [ ] T054 [US5] Make favorite and unfavorite idempotent by relying on the composite primary key rather than pre-existence checks, so a repeat call is a success rather than a duplicate-key error (depends on T053)
- [ ] T055 [US5] Confirm `DeleteAsync` contains **no** favorites-cleanup code and that removal comes solely from the FK cascade defined in `PromptDbContext` — Principle V, FR-017 (depends on T016, T024)
- [ ] T056 [US5] Wire `GET /api/prompts/favorites`, `POST /api/prompts/{id}/favorite`, and `DELETE /api/prompts/{id}/favorite` in `src/EnterpriseAIPlatform.Web/Endpoints/Prompts/PromptEndpoints.cs` per contracts/route-table.md (depends on T028, T053)
- [ ] T057 [US5] Add a favorite toggle and a favorites filter to `src/EnterpriseAIPlatform.Web/Components/Prompts/PromptLibrary.razor` (depends on T029, T056)

**Checkpoint**: Favorites work per-user and disappear structurally on delete.

---

## Phase 8: User Story 6 - Launch a chat pre-seeded from a saved prompt (Priority: P2)

**Goal**: Selecting a prompt populates the chat composer with its description, verbatim.

**Independent Test**: Select a saved prompt from the library and confirm the chat input is populated with the prompt's `description` verbatim, matching the server-side record.

### Tests for User Story 6

- [ ] T058 [P] [US6] Write `ChatComposerStateSeedTests` in `tests/EnterpriseAIPlatform.UnitTests/Chat/ChatComposerStateSeedTests.cs` asserting `SeedFromPrompt` assigns `Description` with zero substitution, including descriptions containing `[bracket]` text, `{brace}` text, and newlines, all of which must survive unchanged — SC-008, FR-012
- [ ] T059 [P] [US6] Add a case asserting `SeedFromPrompt` raises `OnChanged` so a subscribed composer re-renders

### Implementation for User Story 6

- [ ] T060 [US6] Add `SeedFromPrompt(PromptModel prompt)` to `src/EnterpriseAIPlatform.Web/Services/ChatComposerState.cs`, assigning `prompt.Description` to `ComposerText` verbatim and raising `OnChanged` (research.md D11)
- [ ] T061 [US6] Add a "use this prompt" action to `src/EnterpriseAIPlatform.Web/Components/Prompts/PromptLibrary.razor` that calls `SeedFromPrompt` and navigates to the chat surface (depends on T029, T060)
- [ ] T062 [US6] Verify the seeded text flows through spec 024's **existing** chat send path with no second send route introduced — Principle IV (depends on T061)
- [ ] T063 [US6] Confirm `SeedFromPrompt` is reachable from any prompt-referencing entry point, not only the library, satisfying FR-018 without implementing landing-action configuration (out of scope, spec 021) (depends on T060)

**Checkpoint**: All six user stories are independently functional.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [ ] T064 [P] Write `PromptSingleImplementationTests` in `tests/EnterpriseAIPlatform.ArchitectureTests/PromptSingleImplementationTests.cs` asserting exactly one `IPromptService` implementation, that `PromptService` depends on `ISharingPolicyService`, and that no prompt-local sharing evaluator exists — Principle IV
- [ ] T065 [P] Add an architecture test asserting no `ResponseStatus.NOT_FOUND` is returned from any resource-gated prompt path — FR-009
- [ ] T066 [P] Update `README.md` with the Prompts feature, the `PromptSql:ConnectionString` setting, and the two migration commands from quickstart.md
- [ ] T067 Run the full `dotnet test EnterpriseAIPlatform.slnx` suite and confirm every pre-existing test from specs 002/004/006/014/017/018/024/009 passes **unmodified** against the T001 baseline — the hard acceptance condition for the cross-spec edits in T008/T009 and T044/T045
- [ ] T068 Execute the manual walkthrough and the non-revealing-error spot check in [quickstart.md](./quickstart.md), confirming SC-001 through SC-008
- [ ] T069 Open a pull request to `main` from `016-prompt-crud-sharing-ownership-transfer`, calling out the two cross-spec ripples (`UserModel.GroupTokens`, the `ModelAccessDbContext` additive migration) in the description

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup — **BLOCKS all user stories**
- **User Stories (Phases 3–8)**: All depend on Foundational
- **Polish (Phase 9)**: Depends on all desired user stories

### User Story Dependencies

- **US4 (P1, Phase 3)**: Depends only on Foundational. **This is the MVP.**
- **US1 (P1, Phase 4)**: Depends on Foundational + US4's `PromptService` core (T024) — there must be a prompt to transfer
- **US2 (P1, Phase 5)**: Depends on US1's `TransferOwnershipAsync` (T033); the concurrency work is meaningless without a transfer path
- **US3 (P2, Phase 6)**: Depends only on Foundational — **fully independent of US1/US2/US4**, and can be built in parallel by a second developer
- **US5 (P2, Phase 7)**: Depends on US4's `PromptService` core (T024); T052 additionally depends on US1's transfer (T033)
- **US6 (P2, Phase 8)**: Depends on US4's library UI (T029) for the entry point; `SeedFromPrompt` itself (T060) depends only on Foundational

### Within Each User Story

- Tests are written before the implementation they cover, and must fail first
- Entities → evaluator/validation → service → endpoints → UI
- Story complete before moving to the next priority

### Parallel Opportunities

- T002 and T003 run in parallel during Setup
- All four domain entities (T004–T007) run in parallel
- The two unit-test tasks T013 and T014 run in parallel once T011/T012 land
- All test-authoring tasks within a story phase are marked [P] — they touch different files
- **US3 (Phase 6) is the strongest parallel candidate**: it touches only `ModelAccess` and generation files, sharing no file with US4/US1/US2/US5/US6
- T064, T065, and T066 run in parallel during Polish

---

## Parallel Example: Foundational Entities

```bash
# Launch all four domain entities together (Phase 2):
Task: "Create PromptShareTarget in src/EnterpriseAIPlatform.Domain/Prompts/PromptShareTarget.cs"
Task: "Create PromptModel in src/EnterpriseAIPlatform.Domain/Prompts/PromptModel.cs"
Task: "Create PromptFavorite in src/EnterpriseAIPlatform.Domain/Prompts/PromptFavorite.cs"
Task: "Create the request/response DTOs in src/EnterpriseAIPlatform.Domain/Prompts/PromptContracts.cs"
```

## Parallel Example: User Story 4 Tests

```bash
# Launch all three integration test suites for US4 together:
Task: "Write PromptCrudTests in tests/EnterpriseAIPlatform.IntegrationTests/Prompts/PromptCrudTests.cs"
Task: "Write PromptWriteAuthorizationTests in tests/EnterpriseAIPlatform.IntegrationTests/Prompts/PromptWriteAuthorizationTests.cs"
Task: "Write PromptEnumerationTests in tests/EnterpriseAIPlatform.IntegrationTests/Prompts/PromptEnumerationTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 4 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories)
3. Complete Phase 3: User Story 4 (CRUD)
4. **STOP and VALIDATE**: create, edit, delete a prompt through the library UI; confirm unauthorized callers are refused
5. Demo — this is a genuinely usable increment on top of spec 024's shipped chat UI

### Incremental Delivery

1. Setup + Foundational → foundation ready
2. US4 → CRUD works → **MVP demo**
3. US1 + US2 → transfer is secure and atomic → P1 band complete, spec's original bug narrative fully addressed
4. US5 → favorites
5. US6 → prompt-to-chat, the "use a prompt" payoff journey
6. US3 → generation error-format consistency (independent; can land at any point after Foundational)

### Parallel Team Strategy

1. Team completes Setup + Foundational together — note T008/T009 touch shared identity code, so land them before branching out
2. Then:
   - Developer A: US4 → US1 → US2 (the ownership chain)
   - Developer B: US3 (fully independent — no shared files)
   - Developer C: joins after US4's T024 lands, taking US5 then US6

---

## Notes

- [P] tasks = different files, no dependencies
- Verify tests fail before implementing
- Commit after each task or logical group
- **Cross-spec caution**: T008/T009 modify spec 002's identity foundation and T044/T045 modify spec 014's shipped entity. Both must be additive — T010 and T067 exist specifically to prove no prior spec's tests were edited to accommodate them. If either forces a prior test change, stop and reconsider (research.md D6 documents an individual-only fallback for the group-token half of FR-002)
- **Do not** add a `NOT_FOUND` branch to any gated prompt path, however natural it looks — T023 and T065 guard against it (FR-009)
