# Implementation Plan: Session Resolution & Role Derivation

**Branch**: `002-session-role-derivation` | **Date**: 2026-07-21 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-session-role-derivation/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

This is the platform's **foundation feature (Layer 0 of [the sequencing plan](../../docs/spec-sequencing-plan.md))**. It establishes the single canonical server-side session-resolution and role-derivation layer that every later feature depends on, and the walking-skeleton .NET solution structure they build on.

The spec's source facts describe the legacy Next.js/NextAuth reference implementation (three duplicated `impersonateAsStudent` downgrade sites, a raw-throwing `getCurrentUser()`, a client-only `RefreshAccessTokenError` flag, raw-email partition keys). Per the constitution's Technology Stack section, the target build re-homes these requirements on **.NET 10 / Blazor Web App (Interactive Server) / Microsoft.Identity.Web against Microsoft Entra ID**:

- One claims-transformation path derives role flags from Entra group claims and applies the impersonation downgrade exactly once (FR-002/003/004), consumed by a single `ICurrentUserAccessor` (FR-001).
- Session-resolution failures return a structured `ServerActionResponse` result rather than a thrown exception (FR-005/006), and fail **closed** (FR-009).
- Route and admin authorization are enforced server-side via ASP.NET Core authorization policies; UI gating is advisory only (FR-011/012/013).
- Token-refresh failure triggers an explicit re-authentication challenge instead of a silently-broken session (FR-007/008).
- User-scoped storage uses a deterministic hash of the normalized email as the Cosmos partition key (FR-014).

## Technical Context

**Language/Version**: C# on **.NET 10** (current LTS, GA 2025-11-11) — primary language per constitution.

**Primary Dependencies**: ASP.NET Core + **Blazor Web App** (Interactive Server render mode); **Microsoft.Identity.Web** (OIDC sign-in against Entra ID + token acquisition/refresh) and `Microsoft.Identity.Web.UI`; `Azure.Identity` (`DefaultAzureCredential` / workload identity for downstream Azure resources); `Microsoft.Azure.Cosmos` (user-scoped partitioned storage); `Microsoft.Extensions.*` (DI, Options + `ValidateOnStart`, Logging); OpenTelemetry + Application Insights exporter for telemetry.

**Storage**: **Azure Cosmos DB** for user-scoped documents, partition-keyed by a hashed identity. This slice defines the partition-key **hashing contract** and the session/current-user model; it does not own a large persisted domain. No Azure SQL/EF Core entities in this feature (those arrive with personas/prompts, specs 009/016).

**Testing**: **xUnit**; `WebApplicationFactory<TEntryPoint>` for route-protection / admin-gating / no-session integration tests; **bUnit** for the Blazor `AuthenticationStateProvider`-driven auth components; NSubstitute for fakes; a reflection-based **architecture test** to assert a single downgrade implementation exists (SC-002).

**Target Platform**: Linux containers on Azure (App Service or Container Apps) fronted by Entra ID; **Azure SignalR Service** recommended to scale Blazor Server circuits.

**Project Type**: Web application — server-rendered Blazor plus server-side application/infrastructure services, organized as a layered monolith (the platform walking skeleton).

**Performance Goals**: Session/role resolution adds <5 ms p95 per request excluding first-time token acquisition; role/claims transformation is cached per authenticated request/circuit.

**Constraints**: Every authorization decision server-side (Principle II); downgrade evaluation fails closed (FR-009); no provider secret or elevated flag ever reaches the client; identity hashing is deterministic across email casing/whitespace (SC-008).

**Scale/Scope**: Internal enterprise LOB app. This feature: 6 user stories, FR-001–FR-014, SC-001–SC-008. Foundational — establishes structure reused by ~23 downstream specs.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluated against constitution v1.2.1.

| Principle / Constraint | Assessment | Verdict |
|---|---|---|
| **I. Azure-Only, No AWS Vestiges** | Entra ID, Cosmos DB, Key Vault, workload identity. Greenfield .NET code; no S3/Bedrock/SQS/Lambda shapes introduced. | ✅ PASS |
| **II. Explicit, Server-Side Authorization** | The feature's core: one canonical `ICurrentUserAccessor` + ASP.NET Core authorization policies gate every route/admin/model/tool/sharing decision server-side (FR-011/012/013); UI gating is advisory only. | ✅ PASS |
| **III. Fail Loud, Never Fabricate Success** | No-session lookups return a structured `UNAUTHORIZED`/`ERROR` (FR-005) instead of a raw throw; token-refresh failure surfaces an explicit re-auth challenge (FR-007) rather than a silent broken session. | ✅ PASS |
| **IV. One Implementation Per Concern** | Single impersonation-downgrade implementation (FR-002/003), single group→role mapping (FR-004), single current-user resolver (FR-001), single identity hasher (FR-014) — enforced by an architecture test (SC-002). This principle is the feature's spine. | ✅ PASS |
| **V. Schema-Enforced, Not UI-Enforced, Validation** | The Entra group-GUID→role-flag mapping is bound via Options with `ValidateOnStart`; the partition-key hashing rule is enforced structurally at the persistence boundary, not per call site. | ✅ PASS |
| **VI. Testable, EARS-Style Requirements** | Spec FRs are already EARS/`MUST`; plan preserves falsifiability via xUnit + `WebApplicationFactory` + architecture tests, one independent test per story. | ✅ PASS |
| **Tech Stack Alignment** | .NET 10 / Blazor Interactive Server / Microsoft.Identity.Web / Entra ID — matches the constitution's stack directly. | ✅ PASS |
| **Security & Compliance Constraints** | Identity for any external side effect derived server-side from the verified session; secrets excluded structurally (never surfaced to `UserModel`/client); no roster/student-list capability introduced. | ✅ PASS |

**Result**: No violations. Complexity Tracking is intentionally empty.

**Post-design re-check (after Phase 1)**: Still PASS. The design added no new projects beyond the layered walking skeleton and no new cross-cutting duplication; the contracts reinforce Principles II (policy-based server-side gate) and IV (single `ICurrentUserAccessor` / `RoleDowngrade` / `IIdentityHasher`, guarded by an architecture test).

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

This feature establishes the **walking-skeleton solution** every later spec extends. Layered (Domain / Application / Infrastructure / Web) so the canonical session, role, and authorization services live below the Blazor host and are reusable by all future feature modules.

```text
EnterpriseAIPlatform.sln
src/
├── EnterpriseAIPlatform.Web/               # Blazor Web App (Interactive Server) host + composition root
│   ├── Program.cs                          # DI wiring, auth, authorization policies, telemetry
│   ├── Components/                          # App/Routes, auth-state UI, re-auth prompt
│   └── Authentication/                      # Entra OIDC config, sign-in/sign-out/challenge endpoints, public-route allow-list
├── EnterpriseAIPlatform.Application/        # Contracts + orchestration (no framework/Azure deps)
│   ├── Identity/                            # ICurrentUserAccessor, UserModel, IIdentityHasher (contract)
│   ├── Authorization/                       # PolicyNames, IRoleResolver (contract), RoleDowngrade (single impl)
│   └── Common/                              # ServerActionResponse<T>, ResponseStatus enum
├── EnterpriseAIPlatform.Infrastructure/     # Azure/framework implementations
│   ├── Authentication/                      # Microsoft.Identity.Web wiring, IClaimsTransformation (maps groups + applies downgrade once), token-refresh handling
│   ├── Identity/                            # IdentityHasher (SHA-256 of normalized email), partition-key provider
│   ├── Persistence/                         # Cosmos client bootstrap (user-scoped container)
│   └── Telemetry/                           # OpenTelemetry + Application Insights
└── EnterpriseAIPlatform.Domain/            # Role flags, identity value objects (thin in this slice)
tests/
├── EnterpriseAIPlatform.UnitTests/          # role mapping, downgrade idempotency + fail-closed, identity-hash determinism
├── EnterpriseAIPlatform.IntegrationTests/   # WebApplicationFactory: public vs protected routes, admin gating, no-session response shape
└── EnterpriseAIPlatform.ArchitectureTests/  # asserts single downgrade impl + single current-user resolver (SC-002)
```

**Structure Decision**: Layered web-application monolith. Chosen over a single-project layout because the constitution's "one implementation per concern" (IV) and server-side-authorization boundary (II) are best guaranteed by placing the canonical session/role/authorization services in `Application`/`Infrastructure`, referenced by every future feature module, rather than co-locating them in the Blazor host. Downstream specs add feature modules (e.g., Chat, Personas) as new projects/folders alongside these without duplicating the auth spine.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No constitution violations — this section is intentionally empty.

## Phase 0 & 1 Artifacts

- [research.md](./research.md) — technology decisions (Phase 0)
- [data-model.md](./data-model.md) — entities & validation (Phase 1)
- [contracts/](./contracts/) — service interfaces, authorization-policy catalog, route table (Phase 1)
- [quickstart.md](./quickstart.md) — end-to-end validation guide (Phase 1)
