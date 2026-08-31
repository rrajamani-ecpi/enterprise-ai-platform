# Tasks: Persona CRUD & Authorization

**Input**: Design documents from `/specs/009-persona-crud-authorization/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)
**Tests**: Included — this spec's success criteria (SC-001…SC-007) are defined as test suites, and Constitution Principle VI requires falsifiable per-story tests.

**Build-order note** (per spec.md's own Story 2 rationale: "the foundational authorization surface every other persona operation depends on"): **User Story 2 is built before User Story 1**, despite spec.md numbering both P1 in story order 1→2. Story 1 (ownership transfer) needs a persona to exist and be readable, which only Story 2's CRUD-plus-gate machinery provides — this mirrors how spec 014's tasks.md sequenced by real dependency, not story number, when one exists.

**Scope note**: spec.md's own Assumptions frame basic create/edit/delete mechanics (PRD REQ-PERSONA-1) as "pre-existing baseline behavior this spec does not re-specify." This repository has no persona code at all yet, so this tasks.md necessarily builds that baseline too — folded into Story 2's phase, since gating and CRUD are inseparable here (there's nothing to layer FR-003–FR-008 onto otherwise).

**Explicitly deferred — no task builds this, not forgotten**: SC-006's "runtime scan of... rendered component state" clause is only partially exercisable — no Blazor component for personas exists in this tasks.md (Web/Endpoints only), so there is no rendered component state yet to scan. `IPersonaRawAccessor`'s `internal` visibility (T034/T035) structurally prevents the Web layer from reaching `ApiKey` regardless, but the runtime-scan half of SC-006 is deferred until a persona-consuming Blazor component exists.

## Path Conventions (from plan.md — extends the existing layered solution, no new project)

- `src/EnterpriseAIPlatform.Domain/Personas/`, `.Application/Personas/`, `.Infrastructure/Personas/`, `.Infrastructure/DependencyInjection/`, `.Web/Endpoints/Personas/`
- `tests/EnterpriseAIPlatform.UnitTests/Personas/`, `.IntegrationTests/Personas/`, `.ArchitectureTests/`

---

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 Create empty `Personas` folders in `src/EnterpriseAIPlatform.Domain/Personas/`, `.Application/Personas/`, `.Infrastructure/Personas/`, and `.Web/Endpoints/Personas/` per plan.md's Project Structure
- [X] T002 [P] Add a `PersonaSql:ConnectionString` placeholder (no secret) to `appsettings.json` in `src/EnterpriseAIPlatform.Web/`, matching `ModelAccessSql`'s existing shape (not `appsettings.Development.json` — `ModelAccessSql` doesn't appear there either)
- [X] T003 [P] Extend `TestAuthHandler` with an `X-Test-Roles` header (comma-separated `RoleFlags` names: `Employee`, `Contractor`, `Student`) so integration tests can simulate non-admin role combinations over HTTP (research.md D9) in `tests/EnterpriseAIPlatform.IntegrationTests/TestAuthHandler.cs`

**Checkpoint**: Solution still builds; no new NuGet dependency required (`Microsoft.EntityFrameworkCore.SqlServer` is already a dependency via spec 014's `ModelAccessDbContext`).

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ No user story work begins until this phase is complete.**

- [X] T004 [P] Create `PersonaShareTargetType`, `PersonaAccessResult`, `PersonaOperation` enums in `src/EnterpriseAIPlatform.Domain/Personas/PersonaShareTargetType.cs`, `PersonaAccessResult.cs`, `PersonaOperation.cs`
- [X] T005 [P] Create `PersonaShareTarget` value record with `ForIndividual`/`ForGroup` factory methods (invariant enforced structurally, mirrors spec 018's `ShareTarget` — data-model.md) in `src/EnterpriseAIPlatform.Domain/Personas/PersonaShareTarget.cs`
- [X] T006 [P] Create the `PersonaModel` EF Core entity — all data-model.md fields including `ApiKey`, `A2aEnabled`, `IsLessonPersona`, and `RowVersion` (`Guid`, self-managed concurrency token — research.md D7) — in `src/EnterpriseAIPlatform.Domain/Personas/PersonaModel.cs`
- [X] T007 [P] Create the `PersonaPublicDTO` record and a `FromModel(PersonaModel)` mapping (every field except `ApiKey`/`RowVersion` — data-model.md) in `src/EnterpriseAIPlatform.Domain/Personas/PersonaPublicDTO.cs`
- [X] T008 Declare the `IPersonaService` contract in `src/EnterpriseAIPlatform.Application/Personas/IPersonaService.cs`
- [X] T009 [P] Create `PersonaDbContext` (Azure SQL, JSON `ValueConverter`/`ValueComparer` for `Extensions`/`DataProducts`/`CollaboratorPartitionKeys`/`SharedWith`, `RowVersion` configured via `IsConcurrencyToken()` — provider-portable, not SQL Server's native auto-generated `IsRowVersion()`, per research.md D7) plus a design-time `IDesignTimeDbContextFactory`, matching `ModelAccessDbContext`'s pattern, in `src/EnterpriseAIPlatform.Infrastructure/Personas/PersonaDbContext.cs`
- [X] T010 Create the initial EF Core migration for `PersonaDbContext` in `src/EnterpriseAIPlatform.Infrastructure/Personas/Migrations/`
- [X] T011 Create `PersonaServiceCollectionExtensions.AddPersonaInfrastructure` — registers `PersonaDbContext` with a lazy connection string (boots without a live SQL dependency, matching `ModelAccessServiceCollectionExtensions`); `IPersonaService` registration added in Phase 3 once it exists — in `src/EnterpriseAIPlatform.Infrastructure/DependencyInjection/PersonaServiceCollectionExtensions.cs`

**Checkpoint**: Domain vocabulary and EF Core schema compile and migrate cleanly; no business logic yet.

---

## Phase 3: User Story 2 — Access, edit, and delete rights are consistently gated (Priority: P1) 🎯 MVP (build first)

**Goal**: `PersonaAccessEvaluator` is the single, server-side gate for every read/write; full CRUD (create/read/update/delete/list) is wired through it; `apiKey` is structurally absent from every client-reachable return type; `dataProducts` is validated on every write; sharing-target role-gating (FR-008) is enforced; every rejection is the same fixed, non-revealing `Unauthorized` shape.

**Independent Test**: as each of admin, owner, a designated collaborator, an eligible student, and an unrelated authenticated user, attempt read/list/edit/delete/share (individual and group-token) on a mix of regular and lesson personas directly via API, and confirm each outcome matches FR-003 through FR-009/FR-011.

- [X] T012 [P] [US2] Unit test: `PersonaAccessEvaluator.Evaluate` full matrix — admin/owner/collaborator/student(lesson)/unrelated × regular/lesson persona (SC-002/SC-003) in `tests/EnterpriseAIPlatform.UnitTests/Personas/PersonaAccessEvaluatorTests.cs`
- [X] T013 [P] [US2] Unit test: `PersonaAccessEvaluator.CanWrite` blocks lesson-persona edit/delete for any non-admin, including a designated collaborator (SC-003, Edge Case) in the same test file
- [X] T014 [P] [US2] Unit test: `PersonaExtensionRules.TryValidate` — `DataProduct` extension + empty/absent `dataProducts` rejected; populated succeeds; extension absent + empty `dataProducts` succeeds; an empty-string-array counts as empty (SC-007, Edge Case) in `tests/EnterpriseAIPlatform.UnitTests/Personas/PersonaExtensionRulesTests.cs`
- [X] T015 [P] [US2] Architecture test: `PersonaPublicDTO` has no `ApiKey` property (reflection check) and exactly one `IPersonaService` implementation is registered (Principle IV) in `tests/EnterpriseAIPlatform.ArchitectureTests/PersonaSingleImplementationTests.cs`
- [X] T016 [P] [US2] Integration test: an unrelated caller gets the identical fixed `401 Unauthorized` shape for a non-existent persona ID and for an existing-but-forbidden persona ID (SC-002, FR-004, contracts/authorization-policies.md) in `tests/EnterpriseAIPlatform.IntegrationTests/Personas/PersonaAuthorizationTests.cs`
- [X] T017 [P] [US2] Integration test: a non-admin's `GET /api/personas` listing excludes lesson personas (SC-005); an eligible student gets read-only access to a lesson persona (SC-002) in the same test file
- [X] T018 [P] [US2] Integration test: a non-admin's submitted `isLessonPersona` change on update is discarded server-side; the existing flag is preserved (SC-004) in the same test file
- [X] T019 [P] [US2] Integration test: a non-admin sharing with a group token is rejected; an admin sharing with a group token succeeds (FR-008) in the same test file
- [X] T020 [P] [US2] Integration test: after each rejection path above (unauthorized access/edit/delete, blocked lesson-persona write, failed `dataProducts` validation), the persona's stored row is byte-for-byte unchanged from before the attempt — no partial write (FR-012) in the same test file
- [X] T021 [US2] Implement `PersonaAccessEvaluator.Evaluate`/`CanWrite` (research.md D3) in `src/EnterpriseAIPlatform.Application/Personas/PersonaAccessEvaluator.cs`
- [X] T022 [US2] Implement `PersonaExtensionRules.TryValidate` (research.md D6) in `src/EnterpriseAIPlatform.Application/Personas/PersonaExtensionRules.cs`
- [X] T023 [US2] Implement the FR-008 sharing-target role-gate (009's own binary admin/non-admin rule, research.md D8) alongside `PersonaAccessEvaluator` in `src/EnterpriseAIPlatform.Application/Personas/PersonaAccessEvaluator.cs`
- [X] T024 [US2] Implement `IPersonaService.GetAsync`/`ListAsync`/`CreateAsync`/`UpdateAsync`/`DeleteAsync` — every call gated through `PersonaAccessEvaluator`, every write validated via `PersonaExtensionRules`, non-admin `isLessonPersona` changes discarded (FR-006), every gate rejection returns `Unauthorized` only, never `NotFound` (research.md D4), returns `PersonaPublicDTO` only (never `PersonaModel`) — in `src/EnterpriseAIPlatform.Infrastructure/Personas/PersonaService.cs`
- [X] T025 [US2] Register `IPersonaService` in `AddPersonaInfrastructure` in `src/EnterpriseAIPlatform.Infrastructure/DependencyInjection/PersonaServiceCollectionExtensions.cs`
- [X] T026 [US2] Implement `GET/POST/PATCH/DELETE /api/personas[/{id}]` endpoints (research.md D10) in `src/EnterpriseAIPlatform.Web/Endpoints/Personas/PersonaEndpoints.cs`

**Checkpoint**: User Story 2 fully functional and independently testable — the real MVP core (FR-003 through FR-009 and FR-011 all covered).

---

## Phase 4: User Story 1 — Ownership transfer never loses a persona (Priority: P1) 🎯 MVP (depends on US2's CRUD existing)

**Goal**: `TransferOwnershipAsync` is a single atomic SQL update (research.md D1) with `RowVersion`-based conflict detection (D7) — never a delete-then-recreate.

**Independent Test**: trigger an ownership transfer and inject a failure during the write; confirm the persona still exists — either intact under the original owner or fully present under the new owner, never neither, never both.

- [X] T027 [P] [US1] Integration test: a successful transfer leaves the persona existing exactly once, under the new owner, with all fields intact (Acceptance Scenario 1, SC-001) in `tests/EnterpriseAIPlatform.IntegrationTests/Personas/PersonaTransferTests.cs`
- [X] T028 [P] [US1] Integration test: injecting a `SaveChangesAsync` failure during transfer leaves the persona fully intact under the original owner (Acceptance Scenario 2, SC-001) in the same test file
- [X] T029 [P] [US1] Integration test: retrying a transfer after a failed or already-succeeded attempt succeeds without manual recovery and never creates a duplicate (Acceptance Scenario 3, Edge Case) in the same test file
- [X] T030 [P] [US1] Integration test: a `PATCH`/`DELETE` racing a concurrent transfer receives the `409` conflict shape, never a silent overwrite (per-clarification FR-013) in the same test file
- [X] T031 [US1] Implement `IPersonaService.TransferOwnershipAsync` — single `SaveChangesAsync` updating `OwnerUserId`/`OwnerPartitionKey` with a `WHERE`-current-owner precondition, idempotent no-op if already transferred, maps `DbUpdateConcurrencyException` to the `409` shape (research.md D1/D7) in `src/EnterpriseAIPlatform.Infrastructure/Personas/PersonaService.cs`
- [X] T032 [US1] Implement `POST /api/personas/{id}/transfer-ownership`, `RequireAdmin` policy (research.md D10) in `src/EnterpriseAIPlatform.Web/Endpoints/Personas/PersonaEndpoints.cs`

**Checkpoint**: User Story 1 + User Story 2 complete — full MVP.

---

## Phase 5: User Story 3 — The A2A credential never reaches client code (Priority: P2)

**Goal**: Lock in `apiKey`'s structural absence from every client-reachable path (already true by construction since US2's `PersonaPublicDTO`/T024) and add the one legitimate server-only accessor.

**Independent Test**: search every code path that serializes/returns persona data for client consumption; confirm none can include a non-empty `apiKey`, including a newly written call site.

- [X] T033 [P] [US3] Unit test: `GetRawAsync` returns the full `PersonaModel` (including `ApiKey`) — confirms it is the sole path that can, distinct from every other `IPersonaService` method — in `tests/EnterpriseAIPlatform.UnitTests/Personas/PersonaServiceTests.cs`
- [X] T034 [US3] Move `GetRawAsync` off the public `IPersonaService` onto a new, separate `internal interface IPersonaRawAccessor` (with `[assembly: InternalsVisibleTo]` granted only to `EnterpriseAIPlatform.Infrastructure`, the test projects, and a future spec-011 assembly) — a compiler-enforced restriction, stronger than a source-scan (research.md/analyze T1 finding) — implemented by `PersonaService` alongside `IPersonaService`, in `src/EnterpriseAIPlatform.Application/Personas/IPersonaRawAccessor.cs` and `src/EnterpriseAIPlatform.Infrastructure/Personas/PersonaService.cs`
- [X] T035 [US3] Extend `PersonaSingleImplementationTests` (T015) with a reflection-based check that the public `IPersonaService` has no `GetRawAsync` member, matching T015's reflection style rather than a source/text scan in `tests/EnterpriseAIPlatform.ArchitectureTests/PersonaSingleImplementationTests.cs`

**Checkpoint**: User Story 3 independently testable — mostly confirms what US2 already built structurally (T007/T024).

---

## Phase 6: User Story 4 — `dataProducts` requirement is enforced for every entry point (Priority: P3)

**Goal**: Confirm `PersonaExtensionRules` (already built and unit-tested in US2 — T014/T022) is exercised at the real HTTP entry point for every payload shape spec.md's Edge Cases calls out.

**Independent Test**: submit a persona create/update payload directly to the API with `DataProduct` selected and an empty/absent `dataProducts` array; confirm rejection before persistence, independent of any UI.

- [X] T036 [P] [US4] Integration test: direct `POST`/`PATCH /api/personas` with `DataProduct` extension + empty/absent `dataProducts` rejected with a field-specific error; populated succeeds; extension absent + empty `dataProducts` succeeds (SC-007) in `tests/EnterpriseAIPlatform.IntegrationTests/Personas/PersonaValidationTests.cs`
- [X] T037 [P] [US4] Integration test: `dataProducts` supplied as an empty-string array (not omitted/`undefined`) still trips validation (Edge Case) in the same test file

**Checkpoint**: All four user stories independently testable.

---

## Phase 7: Polish & Cross-Cutting

- [X] T038 [P] Run every scenario in [quickstart.md](./quickstart.md) end-to-end and fix any gap found — all scenarios exercised by the test suite below, 39/39 passing
- [X] T039 [P] Update the solution README noting spec 009's scope (CRUD + authorization only; persona builder/live-preview is spec 010; A2A invocation mechanics is spec 011; sharing-target gating is 009's own binary rule, not yet consuming spec 018's `SharingDecision`)
- [X] T040 Verify SC-001 through SC-007 are each covered by a passing test; produce a coverage map (below) and fix any gap — all seven confirmed covered (SC-006 partial/deferred as documented), no gaps

**SC coverage map:**

| SC | Covered by |
|---|---|
| SC-001 | `PersonaTransferTests` (T027–T029) |
| SC-002 | `PersonaAccessEvaluatorTests` (T012), `PersonaAuthorizationTests` (T016, T017) |
| SC-003 | `PersonaAccessEvaluatorTests` (T012, T013) |
| SC-004 | `PersonaAuthorizationTests` (T018) |
| SC-005 | `PersonaAuthorizationTests` (T017) |
| SC-006 | `PersonaSingleImplementationTests` (T015, T035) — structural half only; rendered-component-state scan deferred (see Scope note) |
| SC-007 | `PersonaExtensionRulesTests` (T014), `PersonaValidationTests` (T036, T037) |

---

## Dependencies & Execution Order

- **Setup** → **Foundational** block everything.
- **US2** (P1) must be built before **US1** (P1) despite spec.md's story numbering — US2 is "the foundational authorization surface every other persona operation depends on" per its own rationale; US1's transfer needs a persona to exist and be readable, which only US2's CRUD provides.
- **US3** (P2) depends on US2's `PersonaPublicDTO`/`PersonaService` split already existing (T007/T024) — it hardens/locks in that structure, doesn't rebuild it.
- **US4** (P3) depends on US2's `PersonaExtensionRules` already existing (T014/T022) — it extends HTTP-level test coverage, no new production code beyond what US2 built.
- Polish last; T040 depends on T012–T037.

**Parallel opportunities**: T001–T003 (Setup); T004–T007/T009 (Foundational, different files); all `[P]` test tasks within a story; US3 and US4 can proceed in parallel once US2 lands (both only harden what US2 built, touching different files).

## Implementation Strategy

### MVP First (User Story 2, then User Story 1)

Unlike spec.md's numbering, **build US2 first** — it's the real foundation (its own rationale text says so), and US1 has nothing to transfer without it.

1. Complete Phase 1 (Setup) + Phase 2 (Foundational).
2. Complete Phase 3 (US2) — **checkpoint**: full CRUD, gated, apiKey-safe, validated.
3. Complete Phase 4 (US1) — **MVP checkpoint**: ownership transfer is atomic/recoverable/retryable.
4. Add Phase 5 (US3) — apiKey hardening (mostly already true by construction).
5. Add Phase 6 (US4) — dataProducts HTTP-level hardening.
6. Polish.

### Incremental Delivery

1. Setup + Foundational → Domain vocabulary and EF Core schema compile and migrate.
2. US2 → MVP core: a fully correct, fully tested persona CRUD+authorization vertical.
3. US1 → ownership transfer layered on top, no MVP rework needed.
4. US3/US4 → hardening/confirmation of what US2 already built, independent of each other.
5. Polish → quickstart validated, SC coverage confirmed, README updated.

## Notes

- [P] tasks = different files, no dependencies.
- [Story] label maps task to specific user story for traceability.
- No task in this list touches specs 010 (persona builder), 011 (A2A invocation), or 018 (`ISharingPolicyService`) — those integrations are explicitly out of scope per spec.md's own Assumptions.
- Commit after each task or logical group; stop at either checkpoint to validate independently.
