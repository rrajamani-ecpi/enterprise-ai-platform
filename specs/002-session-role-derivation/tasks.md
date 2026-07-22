# Tasks: Session Resolution & Role Derivation (Foundation / Walking Skeleton)

**Input**: Design documents from `/specs/002-session-role-derivation/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)
**Tests**: Included — this spec's success criteria (SC-001…SC-008) are defined as test suites, and Constitution Principle VI requires falsifiable per-story tests.

Release 1 note: spec 002 is fully in R1 scope (all P1 stories + supporting P2/P3). This is the platform **walking skeleton** every later feature builds on.

## Implementation status (2026-07-22)

Walking skeleton built and green: `dotnet build` clean; **30/30 tests pass** (Unit 21, Architecture 4, Integration 5).

- ✅ Phase 1 Setup, Phase 2 Foundational — complete
- ✅ US1 (single-source downgrade, SC-001/002), US2 (structured no-session, SC-003), US4 (role mapping, SC-005), US5 (route/admin gating, SC-006/007), US6 (identity hashing, SC-008) — implemented + tested
- ⏳ US3 (token-refresh → forced re-auth) — foundation relies on Microsoft.Identity.Web challenge-on-expiry; full FR-007 behavior lands with downstream token acquisition (specs 004/014)
- Note: solution file is `EnterpriseAIPlatform.slnx` (.NET 10 XML format)

## Path Conventions (from plan.md — layered web app)

- `src/EnterpriseAIPlatform.Domain/`, `.Application/`, `.Infrastructure/`, `.Web/`
- `tests/EnterpriseAIPlatform.UnitTests/`, `.IntegrationTests/`, `.ArchitectureTests/`

---

## Phase 1: Setup (Shared Infrastructure)

- [ ] T001 Create `EnterpriseAIPlatform.sln` and the layered project skeleton (Domain, Application, Infrastructure, Web + UnitTests, IntegrationTests, ArchitectureTests) with project references per plan.md
- [ ] T002 [P] Add NuGet dependencies to each project (Microsoft.Identity.Web(.UI), Azure.Identity, Microsoft.Azure.Cosmos, OpenTelemetry + Azure Monitor exporter, xUnit, bUnit, NSubstitute, FluentAssertions)
- [ ] T003 [P] Add `Directory.Build.props` (`<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, LangVersion) and a .NET `.gitignore`
- [ ] T004 [P] Add `appsettings.json` + `appsettings.Development.json` placeholders (Entra, Cosmos, role-mapping, public-routes) in `src/EnterpriseAIPlatform.Web/` — no secrets

**Checkpoint**: Solution builds empty (`dotnet build`).

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ No user story work begins until this phase is complete.**

- [ ] T005 [P] Create `ResponseStatus` enum + `ServerActionResponse<T>` + `ActionError` record in `src/EnterpriseAIPlatform.Application/Common/`
- [ ] T006 [P] Create `RoleFlags`, `UserModel`, and identity value objects in `src/EnterpriseAIPlatform.Application/Identity/` + `src/EnterpriseAIPlatform.Domain/`
- [ ] T007 [P] Declare contracts `ICurrentUserAccessor`, `IRoleResolver`, `IIdentityHasher`, and `PolicyNames` in `src/EnterpriseAIPlatform.Application/{Identity,Authorization}/`
- [ ] T008 Implement `RoleDerivationMappingOptions` + `PublicRoutesOptions` bound via Options with `ValidateOnStart` in `src/EnterpriseAIPlatform.Infrastructure/Authentication/` and `Program.cs`
- [ ] T009 Implement `IRoleResolver` (group-GUID → `RoleFlags`, single mapping) in `src/EnterpriseAIPlatform.Infrastructure/Authentication/` (needed by US1 transformation)
- [ ] T010 Wire Microsoft.Identity.Web OIDC sign-in against Entra ID (groups claim enabled) in `src/EnterpriseAIPlatform.Web/Program.cs` + `Authentication/`
- [ ] T011 [P] Bootstrap OpenTelemetry + Azure Monitor in `src/EnterpriseAIPlatform.Infrastructure/Telemetry/` and register in `Program.cs`
- [ ] T012 [P] Bootstrap Cosmos client + user-scoped container provider in `src/EnterpriseAIPlatform.Infrastructure/Persistence/`
- [ ] T013 Create the Blazor Web App host (Interactive Server render mode, `App`/`Routes`, auth-state plumbing) in `src/EnterpriseAIPlatform.Web/Components/`

**Checkpoint**: App boots, redirects unauthenticated users to Entra sign-in, telemetry emits.

---

## Phase 3: User Story 1 — Single-source impersonation downgrade (Priority: P1) 🎯 MVP

**Goal**: The impersonation downgrade exists in exactly one place and every resolution path agrees.
**Independent Test**: Invoke the claims-transformation path and the current-user accessor under impersonation; all report fully-downgraded flags; change the shared downgrade once → all reflect it.

- [ ] T014 [P] [US1] Unit tests for `RoleDowngrade` (correct downgrade, idempotency, fail-closed) in `tests/EnterpriseAIPlatform.UnitTests/`
- [ ] T015 [P] [US1] Unit test that the claims transformation + `ICurrentUserAccessor` produce identical downgraded flags (SC-001) in `tests/EnterpriseAIPlatform.UnitTests/`
- [ ] T016 [P] [US1] Architecture test asserting exactly one `RoleDowngrade` implementation and one `ICurrentUserAccessor` (SC-002) in `tests/EnterpriseAIPlatform.ArchitectureTests/`
- [ ] T017 [US1] Implement the single `RoleDowngrade` pure function in `src/EnterpriseAIPlatform.Application/Authorization/`
- [ ] T018 [US1] Implement `IClaimsTransformation` that maps groups (via `IRoleResolver`) and applies `RoleDowngrade` exactly once in `src/EnterpriseAIPlatform.Infrastructure/Authentication/`
- [ ] T019 [US1] Implement `ICurrentUserAccessor` (reads transformed principal; never re-derives) in `src/EnterpriseAIPlatform.Infrastructure/Identity/`
- [ ] T020 [US1] Register transformation + accessor in DI (`Program.cs`)

**Checkpoint**: US1 fully functional and independently testable (MVP).

---

## Phase 4: User Story 2 — Structured no-session result (Priority: P2)

**Goal**: `GetCurrentUser()` returns a structured `UNAUTHORIZED` instead of throwing.
**Independent Test**: Call `GetCurrentUser()` with no session → `ServerActionResponse{Status=UNAUTHORIZED}`, no exception.

- [ ] T021 [P] [US2] Test: no-session `GetCurrentUser()` returns `UNAUTHORIZED` (not throw); distinguishable from other failures (SC-003) in `tests/EnterpriseAIPlatform.UnitTests/`
- [ ] T022 [US2] Ensure `ICurrentUserAccessor` returns `ServerActionResponse` `UNAUTHORIZED` on no session in `src/EnterpriseAIPlatform.Infrastructure/Identity/`

---

## Phase 5: User Story 3 — Token-refresh failure → forced re-auth (Priority: P2)

**Goal**: Refresh failure surfaces an explicit re-auth challenge; success rebuilds a fresh session.
**Independent Test**: Force a refresh failure → next check redirects to sign-in; re-auth → re-derived flags (SC-004).

- [ ] T023 [P] [US3] bUnit/integration test: refresh-failure → re-auth challenge; success → fresh flags (SC-004) in `tests/EnterpriseAIPlatform.IntegrationTests/`
- [ ] T024 [US3] Handle Microsoft.Identity.Web refresh failure → challenge, and re-derive flags on re-auth in `src/EnterpriseAIPlatform.Web/Authentication/`

---

## Phase 6: User Story 5 — Server-side route & admin gating (Priority: P2)

**Goal**: Deny-by-default route protection with an explicit public allow-list; admin gating server-side.
**Independent Test**: Public routes served unauth; other routes redirect; admin route rejects non-admin even when UI exposes it (SC-006/007).

- [ ] T025 [P] [US5] Integration tests (`WebApplicationFactory`): public-vs-protected routing + admin rejection with a UI that exposes the control (SC-006/007) in `tests/EnterpriseAIPlatform.IntegrationTests/`
- [ ] T026 [US5] Configure the fallback authorization policy (deny by default) + public-route allow-list in `Program.cs`
- [ ] T027 [US5] Configure `RequireAdmin` policy and apply to admin routes/actions in `Program.cs`
- [ ] T028 [P] [US5] Add advisory `<AuthorizeView>` UX mirrors (never the boundary) in `src/EnterpriseAIPlatform.Web/Components/`

---

## Phase 7: User Story 4 — Deterministic role mapping (Priority: P3)

**Goal**: Lock in correct group-claim → flag derivation as a regression-tested requirement.
**Independent Test**: Fixtures (admin group, no group, multiple groups) → expected flags (SC-005).

- [ ] T029 [P] [US4] Unit tests for `IRoleResolver` across group fixtures incl. multiple/none (SC-005) in `tests/EnterpriseAIPlatform.UnitTests/`
- [ ] T030 [US4] Harden `IRoleResolver` edge cases (unknown GUIDs ignored, independent flags) in `src/EnterpriseAIPlatform.Infrastructure/Authentication/`

---

## Phase 8: User Story 6 — Hashed storage partition keys (Priority: P3)

**Goal**: User data keyed by a hash of the normalized email, never the raw email.
**Independent Test**: SHA-256 of normalized email; casing/whitespace variants produce identical key (SC-008).

- [ ] T031 [P] [US6] Unit tests for `IIdentityHasher` determinism + never-raw (SC-008) in `tests/EnterpriseAIPlatform.UnitTests/`
- [ ] T032 [US6] Implement `IIdentityHasher` (SHA-256 of lowercase+trim email) + partition-key provider in `src/EnterpriseAIPlatform.Infrastructure/Identity/`

---

## Phase 9: Polish & Cross-Cutting

- [ ] T033 [P] Add minimal liveness/readiness health endpoints (public; cross-ref spec 017) in `src/EnterpriseAIPlatform.Web/`
- [ ] T034 [P] Add solution README (build/test/run) and verify `dotnet build` + `dotnet test` are green
- [ ] T035 Verify SC-001…SC-008 are each covered by a passing test; fix gaps

---

## Dependencies & Execution Order

- **Setup (P1)** → **Foundational (P2)** block everything.
- **US1 (Phase 3)** is the MVP and depends only on Foundational.
- **US2, US3, US5** (P2) depend on US1's accessor/transformation; largely independent of each other.
- **US4** (P3) hardens the resolver implemented in Foundational (T009).
- **US6** (P3) is self-contained.
- Polish last.

**Parallel opportunities**: T002/T003/T004; T005/T006/T007; T011/T012; all `[P]` test tasks within a story; US2/US3/US5 can proceed in parallel once US1 lands.

## Implementation Strategy

Deliver **US1 first as the MVP checkpoint** (single-source downgrade proven end-to-end on the walking skeleton), then layer US2/US3/US5, then P3 hardening (US4/US6), then polish. Keep the architecture test (T016) green throughout to prevent duplication regressions (Principle IV).
