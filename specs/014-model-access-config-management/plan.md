# Implementation Plan: Model & Access Configuration Management

**Branch**: `014-model-access-config-management` | **Date**: 2026-07-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/014-model-access-config-management/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

This is **Layer 1** of [the sequencing plan](../../docs/spec-sequencing-plan.md) — the model registry and access-gating service every model-facing feature (chat/004, persona builder/010, orchestration/001, voice/023, image/022) reads from. It depends only on spec 002 (session/role derivation), already implemented.

Per [`docs/Release1-MVP Plan.md`](../../docs/Release1-MVP%20Plan.md), **R1 scopes this spec to a subset**: model catalog, server-side model-access gating, and workload-identity provider auth (US1, US2, US3, US4, US6-single-provider, US8, and the free-to-fix US5). **Deferred to R2+**: multi-provider request/response adaptation (US7 — Claude/Vertex/DeepSeek/Llama/Mistral/Kimi adapters) and the admin config UI (no Blazor admin screens ship in R1; config is seeded via deployment, mutation APIs exist but are unconsumed by any UI yet). This plan designs the full spec's contracts (so R2 slots in without rework) but flags the R1/R2 boundary explicitly per artifact below.

- One admin-gated mutation surface, reusing spec 002's `PolicyNames.RequireAdmin` policy, protects system-config, model-config, message-limit, and persona-generation-model writes (FR-001).
- Effective model access is computed server-side as `isEnabled ∧ role-allow-listed ∧ (¬requiresAdvancedModelAccess ∨ advancedModelAccess)`, reusing spec 002's `UserModel.IsAdmin`/`AdvancedModelAccess`/role flags (FR-003).
- Model deletion is soft-only (`isDeleted`) (FR-002); message-limit/persona-gen-model reads stay open to any authenticated caller while writes stay admin-gated (FR-004–007); writes are re-validated server-side regardless of client input (FR-008/009).
- `/api/user/preferences/*` gets its own explicit 401 check (FR-010) — built correctly from the start since this is greenfield.
- The model catalog records canonical `provider:modelId`, display name, provider, capability flags, and access tier per model, extensible via config without a code change (FR-011); R1 populates one Azure/Foundry-hosted model, with the schema ready for more.
- A single `IModelProviderAdapter` seam is introduced (FR-012) with one Azure/Foundry implementation in R1; other providers implement the same interface in R2, deferred here.
- Azure/Foundry model calls authenticate via workload/managed identity, never a static key (FR-013).
- System-config reads are served from Redis cache with a hardcoded-default fallback on store/cache unavailability (FR-014).

## Technical Context

**Language/Version**: C# on **.NET 10** — same solution as spec 002; this feature adds project folders, not new projects.

**Primary Dependencies**: Reuses spec 002's `ICurrentUserAccessor`, `PolicyNames`, `ServerActionResponse<T>`, and Azure.Identity workload-identity wiring. New: **Entity Framework Core** + `Microsoft.EntityFrameworkCore.SqlServer` (admin/system config is relational per the constitution's storage guidance); **Azure Cache for Redis** (`Microsoft.Extensions.Caching.StackExchangeRedis`) for the FR-014 cache-with-fallback; an Azure/Foundry model-access SDK (Azure OpenAI / Microsoft Foundry client) authenticated via `DefaultAzureCredential`/workload identity for the R1 provider adapter.

**Storage**: **Azure SQL Database via EF Core** for `SystemModelConfig`, `ModelConfigDocument`, `ModelAliasDocument`, `MessageLimitConfig`, `PersonaGenerationModelConfig` — strongly relational, schema-stable admin/system config, per the constitution's Data & Storage guidance (preferred over Cosmos for this category). **Azure Cache for Redis** fronts system-config reads (FR-014/SC-010); on both SQL and Redis unavailability, reads fall back to hardcoded defaults in code, never a hard failure.

**Testing**: **xUnit** for the access-computation matrix (role × `isEnabled` × `requiresAdvancedModelAccess` × `advancedModelAccess`, SC-002) and server-side re-validation (SC-005); `WebApplicationFactory` integration tests for admin-gate enforcement (SC-001), soft-delete persistence (SC-003), read/write asymmetry (SC-004), and the preferences-route 401 (SC-006); an EF Core in-memory/SQLite provider for repository tests without a live SQL dependency in CI.

**Target Platform**: Same as spec 002 — Linux containers on Azure, fronted by Entra ID via the existing walking skeleton.

**Project Type**: Web application — extends the existing layered monolith (Domain/Application/Infrastructure/Web) established by spec 002; no new top-level project.

**Performance Goals**: Effective-model-access computation adds negligible (<5ms p95) overhead per chat-model-selection call; system-config cache reads avoid a SQL round-trip on the hot path (constitution's Cost Optimization/Performance Efficiency WAF pillars).

**Constraints**: Every mutation and every read-side access decision resolved server-side (Principle II) — client-side admin-UI gating (deferred to R2 anyway) would only ever be advisory; no long-lived provider secret in client-delivered code/config (FR-013); config validation happens in the schema/service layer, not only at a future admin UI (Principle V) — this is why R1 still builds the validated mutation API even with no UI consumer yet.

**Scale/Scope**: Internal enterprise pilot. R1 tasks US1–US6 (single-provider), US8; R2 adds US7's remaining provider adapters and the admin config UI. FR-001–FR-014, SC-001–SC-010.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluated against constitution v1.2.1.

| Principle / Constraint | Assessment | Verdict |
|---|---|---|
| **I. Azure-Only, No AWS Vestiges** | Azure SQL, Azure Cache for Redis, Azure/Foundry model endpoints, workload identity. No AWS-shaped code. | ✅ PASS |
| **II. Explicit, Server-Side Authorization** | Admin gate (FR-001) reuses spec 002's `RequireAdmin` policy; effective model access (FR-003) computed server-side on every read, not cached client-side; no UI gating substitutes for a server check (R1 ships no admin UI at all, removing any temptation to gate client-side). | ✅ PASS |
| **III. Fail Loud, Never Fabricate Success** | Config-store/cache outage falls back to documented hardcoded defaults (FR-014/SC-010) — an explicit, documented fail-open policy per the constitution's carve-out, not a silent fabrication; provider-adapter normalization failures surface a consistent error, never a fabricated success (Edge Cases). | ✅ PASS |
| **IV. One Implementation Per Concern** | One effective-access computation, one soft-delete path, one `IModelProviderAdapter` seam (one implementation per provider, not per call site), one preferences-auth check pattern reused from spec 002's route-gating approach. | ✅ PASS |
| **V. Schema-Enforced, Not UI-Enforced, Validation** | Message-limit integer-≥1 and persona-gen-model allow-list validation live in the Application-layer service, enforced on every write regardless of caller (FR-008/009) — deliberately built before any UI exists, so there is no UI-only validation to bypass. | ✅ PASS |
| **VI. Testable, EARS-Style Requirements** | Spec FRs are EARS/`MUST`; each SC-001…SC-010 maps to a concrete xUnit/integration test. | ✅ PASS |
| **Tech Stack Alignment** | EF Core + Azure SQL for admin/system config, Redis for cache-with-fallback, workload identity for model auth — matches constitution's Data & Storage section directly. | ✅ PASS |
| **Security & Compliance Constraints** | No long-lived provider secret in client-delivered code (FR-013); credentials/API keys excluded structurally from any client-facing accessor (constitution's Security & Compliance Constraints), extended here to model-provider credentials. | ✅ PASS |

**Result**: No violations. Complexity Tracking is intentionally empty. Deferring US7's remaining provider adapters and the admin UI to R2 is a **scope** decision (recorded in Summary and this feature's `tasks.md`), not a constitution exception — the `IModelProviderAdapter` seam and validated mutation API are built now specifically so R2 adds providers/UI without reopening this design.

## Project Structure

### Documentation (this feature)

```text
specs/014-model-access-config-management/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md         # Phase 1 output (/speckit-plan command)
├── contracts/            # Phase 1 output (/speckit-plan command)
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

Extends spec 002's layered solution — no new projects, new folders within the existing four.

```text
EnterpriseAIPlatform.sln
src/
├── EnterpriseAIPlatform.Web/
│   └── Endpoints/ModelAccess/              # Admin mutation endpoints (RequireAdmin), open read endpoints,
│                                            # /api/user/preferences/* explicit 401 check (FR-010)
├── EnterpriseAIPlatform.Application/
│   └── ModelAccess/                        # IModelAccessService (effective-access computation, FR-003),
│                                            # IModelCatalogService, IMessageLimitConfigService,
│                                            # IPersonaGenerationModelConfigService, IModelProviderAdapter (contract)
├── EnterpriseAIPlatform.Infrastructure/
│   ├── ModelAccess/                         # EF Core DbContext + repositories (SystemModelConfig,
│   │                                        # ModelConfigDocument, ModelAliasDocument, MessageLimitConfig,
│   │                                        # PersonaGenerationModelConfig), Redis-backed cache with
│   │                                        # hardcoded-default fallback (FR-014)
│   └── ModelProviders/                     # AzureFoundryProviderAdapter (workload identity, FR-013);
│                                            # other providers implement IModelProviderAdapter in R2
└── EnterpriseAIPlatform.Domain/
    └── ModelAccess/                         # Entities + value objects for the Key Entities in spec.md
tests/
├── EnterpriseAIPlatform.UnitTests/          # access-computation matrix (SC-002), validation (SC-005)
├── EnterpriseAIPlatform.IntegrationTests/   # admin-gate (SC-001), soft-delete (SC-003), read/write
│                                            # asymmetry (SC-004), preferences 401 (SC-006)
└── EnterpriseAIPlatform.ArchitectureTests/  # single IModelProviderAdapter-per-provider, single
                                             # effective-access computation
```

**Structure Decision**: Reuse spec 002's layered monolith rather than a new project — this feature is a shared cross-cutting service (Layer 1), not a standalone app. `ModelAccess` and `ModelProviders` are peer folders to spec 002's `Identity`/`Authentication`, keeping the "one implementation per concern" boundary at the folder level per Principle IV.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No constitution violations — this section is intentionally empty.

## Phase 0 & 1 Artifacts

- [research.md](./research.md) — technology decisions (Phase 0)
- [data-model.md](./data-model.md) — entities & validation (Phase 1)
- [contracts/](./contracts/) — service interfaces, authorization-policy reuse, route table (Phase 1)
- [quickstart.md](./quickstart.md) — end-to-end validation guide (Phase 1)
