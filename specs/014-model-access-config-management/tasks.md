# Tasks: Model & Access Configuration Management (R1 subset)

**Input**: Design documents from `/specs/014-model-access-config-management/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)
**Tests**: Included — this spec's success criteria (SC-001…SC-010) are defined as test suites, and Constitution Principle VI requires falsifiable per-story tests.

**Release 1 scope note** (per [`docs/Release1-MVP Plan.md`](../../docs/Release1-MVP%20Plan.md) and `plan.md`'s Summary): this tasks.md covers **US1, US2, US3, US4, US5, US6 (single Azure/Foundry provider only), and US8**. **Explicitly deferred to R2 — no tasks generated here, not forgotten**:
- **US7's remaining provider adapters** (Claude, Vertex, DeepSeek, Llama, Mistral, Kimi) — the `IModelProviderAdapter` seam is built in this phase (T023-T025) specifically so R2 adds these without rework.
- **Admin config UI** — all mutation endpoints in this phase are API-only; no Blazor admin screens are tasked here.
- **SC-008** (multi-provider client-code claim) is untestable until R2's adapters exist; **SC-010**'s full drill is tasked here (T041) since Redis+SQL fallback doesn't depend on multi-provider work.

## Path Conventions (from plan.md — extends spec 002's layered solution, no new project)

- `src/EnterpriseAIPlatform.Domain/ModelAccess/`, `.Application/ModelAccess/`, `.Infrastructure/{ModelAccess,ModelProviders}/`, `.Web/Endpoints/ModelAccess/`
- `tests/EnterpriseAIPlatform.UnitTests/`, `.IntegrationTests/`, `.ArchitectureTests/`

---

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 Add NuGet dependencies: `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.Extensions.Caching.StackExchangeRedis`, and the Azure/Foundry model-access SDK to `EnterpriseAIPlatform.Infrastructure`
- [X] T002 [P] Add SQL connection string, Redis connection string, and Foundry endpoint placeholders (no secrets) to `appsettings.json` / `appsettings.Development.json` in `src/EnterpriseAIPlatform.Web/`
- [X] T003 [P] Add an EF Core migrations folder and `dotnet-ef` design-time factory in `src/EnterpriseAIPlatform.Infrastructure/ModelAccess/`

**Checkpoint**: Solution still builds with new dependencies restored.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ No user story work begins until this phase is complete.**

- [X] T004 [P] Create `ModelConfigDocument`, `SystemModelConfig`, `ModelAliasDocument`, `MessageLimitConfig`, `PersonaGenerationModelConfig` entities in `src/EnterpriseAIPlatform.Domain/ModelAccess/`
- [X] T005 [P] Declare contracts `IModelAccessService`, `IModelCatalogService`, `IMessageLimitConfigService`, `IPersonaGenerationModelConfigService`, `IModelProviderAdapter` in `src/EnterpriseAIPlatform.Application/ModelAccess/`
- [X] T006 Create `ModelAccessDbContext` (EF Core) with a global query filter excluding `IsDeleted` models, plus initial migration, in `src/EnterpriseAIPlatform.Infrastructure/ModelAccess/`
- [X] T007 Implement the Redis-backed `IDistributedCache` wrapper for `SystemModelConfig` reads with hardcoded-default fallback on cache **and** SQL unavailability (FR-014) in `src/EnterpriseAIPlatform.Infrastructure/ModelAccess/`
- [X] T008 Seed default singleton rows (`SystemModelConfig`, `MessageLimitConfig`, `PersonaGenerationModelConfig`) via migration seed data in `src/EnterpriseAIPlatform.Infrastructure/ModelAccess/`

**Checkpoint**: DbContext migrates cleanly; cache-with-fallback wrapper is unit-testable in isolation.

---

## Phase 3: User Story 1 — Admin-only gate protects every config mutation (Priority: P1) 🎯 MVP

**Goal**: Every system-config, model-config, message-limit, and persona-generation-model mutation is rejected for non-admins and succeeds for admins.
**Independent Test**: Call each of the four mutation endpoints as a non-admin (rejected, no write) and as an admin (succeeds) — SC-001.

- [X] T009 [P] [US1] Integration tests: non-admin rejected (no document change) and admin succeeds on all four mutation endpoints (SC-001) in `tests/EnterpriseAIPlatform.IntegrationTests/`
- [X] T010 [US1] Implement `PUT /api/admin/system-config` endpoint in `src/EnterpriseAIPlatform.Web/Endpoints/ModelAccess/`
- [X] T011 [US1] Implement `PUT /api/admin/model-config/{id}` endpoint in `src/EnterpriseAIPlatform.Web/Endpoints/ModelAccess/`
- [X] T012 [US1] Implement `PUT /api/admin/config/message-limit` endpoint in `src/EnterpriseAIPlatform.Web/Endpoints/ModelAccess/`
- [X] T013 [US1] Implement `PUT /api/admin/config/persona-generation-model` endpoint in `src/EnterpriseAIPlatform.Web/Endpoints/ModelAccess/`
- [X] T014 [US1] Map all four endpoints to spec 002's `PolicyNames.RequireAdmin` policy in `src/EnterpriseAIPlatform.Web/Program.cs`

**Checkpoint**: US1 fully functional and independently testable (MVP).

---

## Phase 4: User Story 2 — Effective model access is computed correctly, and model removal is always reversible (Priority: P1)

**Goal**: Available-models computation matches the FR-003 intersection exactly; model deletion is always soft.
**Independent Test**: Access-matrix fixtures match expected inclusion/exclusion (SC-002); a deleted model persists with `isDeleted: true` (SC-003).

- [X] T015 [P] [US2] Unit tests for `ComputeEffectiveAccess` across the role × `isEnabled` × `requiresAdvancedModelAccess` × `advancedModelAccess` matrix (SC-002) in `tests/EnterpriseAIPlatform.UnitTests/`
- [X] T016 [P] [US2] Integration test: soft-deleted model row persists with `IsDeleted=true`, excluded from catalog listing, never physically removed (SC-003) in `tests/EnterpriseAIPlatform.IntegrationTests/`
- [X] T017 [P] [US2] Architecture test asserting exactly one `ComputeEffectiveAccess`/`IModelAccessService` implementation exists in `tests/EnterpriseAIPlatform.ArchitectureTests/`
- [X] T018 [US2] Implement the single `ComputeEffectiveAccess` pure function in `src/EnterpriseAIPlatform.Application/ModelAccess/`
- [X] T019 [US2] Implement `IModelAccessService.GetAvailableModelsAsync` (reuses spec 002 `UserModel`) in `src/EnterpriseAIPlatform.Infrastructure/ModelAccess/`
- [X] T020 [US2] Implement `IModelCatalogService` (Get/List/Upsert/SoftDelete/ResolveAlias) in `src/EnterpriseAIPlatform.Infrastructure/ModelAccess/`
- [X] T021 [US2] Implement `GET /api/model-access/available-models` and `GET /api/model-catalog` endpoints in `src/EnterpriseAIPlatform.Web/Endpoints/ModelAccess/`
- [X] T022 [US2] Wire `DELETE /api/admin/model-config/{id}` to `SoftDeleteAsync` only (T011's endpoint group)

**Checkpoint**: US1 + US2 functional and independently testable.

---

## Phase 5: User Story 8 — Model endpoint authentication never relies on long-lived secrets (Priority: P1)

**Goal**: The R1 Azure/Foundry provider call authenticates via workload identity; no long-lived secret exists in client-reachable code/config.
**Independent Test**: Inspect the adapter's auth path and all client-delivered config for a static key (SC-009).

- [X] T023 [P] [US8] Integration test asserting `AzureFoundryProviderAdapter` authenticates via `DefaultAzureCredential`/workload identity and no static provider key appears in `appsettings*.json` or client-delivered config (SC-009) in `tests/EnterpriseAIPlatform.IntegrationTests/`
- [X] T024 [US8] Implement `AzureFoundryProviderAdapter` (`IModelProviderAdapter`) using `DefaultAzureCredential`/workload identity in `src/EnterpriseAIPlatform.Infrastructure/ModelProviders/`
- [X] T025 [US8] Register `AzureFoundryProviderAdapter` in DI keyed by `Provider = "azure-foundry"` in `src/EnterpriseAIPlatform.Web/Program.cs`

**Checkpoint**: US1 + US2 + US8 complete — the R1 MVP model-gating vertical is done.

---

## Phase 6: User Story 3 — Message-limit and persona-generation-model configs stay readable while remaining write-protected (Priority: P2)

**Goal**: Both configs are readable by any authenticated user while writes stay admin-gated.
**Independent Test**: Non-admin reads succeed; non-admin writes rejected; admin writes succeed (SC-004).

- [X] T026 [P] [US3] Integration test: non-admin `GET` succeeds and non-admin `PUT` rejected for both message-limit and persona-generation-model endpoints (SC-004) in `tests/EnterpriseAIPlatform.IntegrationTests/`
- [X] T027 [US3] Implement `IMessageLimitConfigService.GetAsync` with fresh-tenant defaults (both caps `null`) in `src/EnterpriseAIPlatform.Infrastructure/ModelAccess/`
- [X] T028 [US3] Implement `IPersonaGenerationModelConfigService.GetAsync` with fresh-tenant default (empty allow-list) in `src/EnterpriseAIPlatform.Infrastructure/ModelAccess/`
- [X] T029 [US3] Implement `GET /api/config/message-limit` and `GET /api/config/persona-generation-model` endpoints (authenticated-only, no `RequireAdmin`) in `src/EnterpriseAIPlatform.Web/Endpoints/ModelAccess/`

**Checkpoint**: US3 independently testable alongside US1/US2/US8.

---

## Phase 7: User Story 4 — Config mutations are re-validated server-side regardless of client input (Priority: P2)

**Goal**: Message-limit caps and persona-generation model selection are enforced server-side on every write.
**Independent Test**: Invalid caps and out-of-allow-list model ids are rejected server-side; valid ones succeed (SC-005).

- [X] T030 [P] [US4] Unit tests: message-limit cap of `0`, negative, and non-integer rejected; ≥1 integer accepted (SC-005) in `tests/EnterpriseAIPlatform.UnitTests/`
- [X] T031 [P] [US4] Unit tests: persona-generation model id outside the allow-list rejected; allow-listed id accepted (SC-005) in `tests/EnterpriseAIPlatform.UnitTests/`
- [X] T032 [US4] Implement `IMessageLimitConfigService.SetAsync` server-side ≥1-integer validation in `src/EnterpriseAIPlatform.Infrastructure/ModelAccess/`
- [X] T033 [US4] Implement `IPersonaGenerationModelConfigService.SetAllowedModelsAsync` and `ValidateSelectionAsync` (fail-closed on empty/misconfigured allow-list) in `src/EnterpriseAIPlatform.Infrastructure/ModelAccess/`

**Checkpoint**: US4 independently testable.

---

## Phase 8: User Story 6 — Model catalog records complete identity and capability metadata (Priority: P3, R1 = single provider)

**Goal**: The one R1-registered model exposes complete, correctly-shaped catalog metadata; the schema is R2-ready without a code change.
**Independent Test**: Catalog entry includes canonical id, display name, provider, capability flags, and access tier (SC-007, single-provider scope).

- [X] T034 [P] [US6] Unit test: `ModelConfigDocument` write rejected if any capability flag (tool-calling/vision/reasoning) is unset rather than defaulted (Edge Case) in `tests/EnterpriseAIPlatform.UnitTests/`
- [X] T035 [P] [US6] Integration test: `GET /api/model-catalog` entry for the R1 model includes canonical `provider:modelId`, display name, provider, all capability flags, and access tier (SC-007) in `tests/EnterpriseAIPlatform.IntegrationTests/`
- [X] T036 [US6] Enforce the required-capability-flag validation in `IModelCatalogService.UpsertAsync` in `src/EnterpriseAIPlatform.Infrastructure/ModelAccess/`
- [X] T037 [US6] Seed the one R1 Azure/Foundry model's registry entry via migration seed data in `src/EnterpriseAIPlatform.Infrastructure/ModelAccess/`

**Checkpoint**: US6 independently testable (single-provider scope).

---

## Phase 9: User Story 5 — User-preference routes return a correct 401 for unauthenticated requests (Priority: P3)

**Goal**: `/api/user/preferences/*` returns 401, not 500, for an unauthenticated caller.
**Independent Test**: Unauthenticated call to any sub-route returns 401 with the `UNAUTHORIZED` shape (SC-006).

- [X] T038 [P] [US5] Integration test parametrized over every `/api/user/preferences/*` sub-route: no session → 401 `UNAUTHORIZED`, never 500; active session → unaffected (SC-006) in `tests/EnterpriseAIPlatform.IntegrationTests/`
- [X] T039 [US5] Add an explicit authenticated-user check (reusing spec 002's fallback-policy pattern) returning `ServerActionResponse` `UNAUTHORIZED` to every `/api/user/preferences/*` route, before the existing internal helper runs, in `src/EnterpriseAIPlatform.Web/Endpoints/`

**Checkpoint**: All R1-scoped stories (US1–US6, US8) independently functional.

---

## Phase 10: Polish & Cross-Cutting

- [X] T040 [P] Unit test (NSubstitute fakes for `IDistributedCache`/`ISystemModelConfigStore`, deterministic vs. a live-infra integration drill) covering the full FR-014/SC-010 fallback chain: Redis unavailable → SQL served; both unavailable → hardcoded defaults served, no hard failure, in `tests/EnterpriseAIPlatform.UnitTests/RedisSystemModelConfigCacheTests.cs`
- [X] T041 [P] Architecture test asserting exactly one `IModelProviderAdapter` implementation is registered per distinct `Provider` value in `tests/EnterpriseAIPlatform.ArchitectureTests/`
- [X] T042 [P] Update the solution README with spec 014's endpoints and the explicit R1 (single-provider, API-only) vs. R2 (remaining adapters, admin UI) scope note
- [X] T043 Verify SC-001…SC-007 and SC-009…SC-010 are each covered by a passing test; fix gaps. (SC-008 remains untestable until R2's adapters exist — confirm it is recorded as deferred, not silently skipped.)

**SC coverage map (verified 2026-07-29, 86/86 tests passing):**
| SC | Covered by |
|---|---|
| SC-001 | `ModelAccessAdminGateTests` |
| SC-002 | `ModelAccessEvaluatorTests` |
| SC-003 | `ModelCatalogSoftDeleteTests`, `ModelCatalogServiceTests` |
| SC-004 | `ConfigReadWriteAsymmetryTests` |
| SC-005 | `MessageLimitConfigServiceTests`, `PersonaGenerationModelConfigServiceTests` |
| SC-006 | `UserPreferencesAuthTests` |
| SC-007 | `ModelCatalogMetadataTests` |
| SC-008 | **Deferred to R2** — untestable until the remaining provider adapters exist; not silently skipped |
| SC-009 | `AzureFoundryProviderAdapterTests` |
| SC-010 | `RedisSystemModelConfigCacheTests` |

---

## Dependencies & Execution Order

- **Setup** → **Foundational** block everything.
- **US1, US2, US8** (all P1) depend only on Foundational and are independently testable; US2's catalog/access services are consumed by US8's adapter registration but neither blocks the other's tests.
- **US3, US4** (P2) depend on Foundational + the entities/services US2 establishes for message-limit/persona-gen-model config, but are independently testable once US2 lands.
- **US6** (P3) hardens the catalog schema/seeding US2 established.
- **US5** (P3) is self-contained — depends only on spec 002's auth pattern.
- Polish last; T040 depends on T007 (Foundational cache wrapper); T041 depends on T024 (US8's adapter).
- **Deferred to R2, not sequenced here**: US7's remaining provider adapters (depend on T005's `IModelProviderAdapter` contract existing, satisfied by this phase); admin config UI (depends on all mutation endpoints in T010-T013, T032-T033 existing, satisfied by this phase).

**Parallel opportunities**: T001/T002/T003; T004/T005; all `[P]` test tasks within a story; US1/US2/US8 can proceed in parallel once Foundational lands; US3/US4/US6/US5 can proceed in parallel once US2 lands.

## Implementation Strategy

Deliver **US1 + US2 + US8 first as the MVP checkpoint** (the full R1 model-gating vertical: admin-gated mutation, correct access computation with reversible deletion, and workload-identity provider auth), then layer US3/US4 (config read/write correctness), then US6/US5 (catalog completeness, preferences fix), then polish. Keep the architecture tests (T017, T041) green throughout to prevent duplication regressions (Principle IV). Do not begin any US7-remaining-adapter or admin-UI work against this tasks.md — that is explicitly R2 scope requiring its own `/speckit-tasks` pass once R1 ships.
