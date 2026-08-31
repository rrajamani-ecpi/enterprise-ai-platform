# Implementation Plan: Persona CRUD & Authorization

**Branch**: `009-persona-crud-authorization` | **Date**: 2026-08-31 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/009-persona-crud-authorization/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

This spec is genuinely greenfield in code — no Persona type exists anywhere in `src/` yet, despite its spec text narrating "as-is" bugs from the legacy accelerator (`EnsurePersonaOperation`, Cosmos delete-then-recreate transfer). The plan below builds the target (to-be) behavior directly, using this platform's actual conventions rather than replicating the legacy Cosmos-based mechanics the spec's bug narrative describes.

- **Storage is Azure SQL via EF Core**, per the constitution's Data & Storage guidance ("personas... strongly relational, schema-stable entities"), not Cosmos. This is the single biggest simplification versus the spec's own framing: User Story 1's "atomic or recoverable" ownership transfer, described against a Cosmos partition-key move, becomes a **single-row `UPDATE`** (`OwnerUserId`/`OwnerPartitionKey` columns) inside one `SaveChangesAsync()` — already atomic by construction, with no delete-then-recreate step and therefore no way to ever end up with zero or two copies of a persona (FR-001/FR-002 hold trivially; see research.md D1/D7).
- **`PersonaAccessEvaluator`** is a new static, pure Application-layer function (mirroring spec 018's `SharingPolicyEvaluator` and spec 014's `ModelAccessEvaluator`) implementing FR-003 through FR-005's admin/owner/collaborator/student rules — no shared "EnsureXOperation" abstraction exists yet in this codebase to reuse; this spec introduces the first one, for personas specifically.
- **Enumeration prevention (FR-004/SC-002)** requires care the naive approach would miss: `ServerActionResponse.NotFound(...)` and `.Unauthorized(...)` have different `Status` values, so returning `NotFound` for "doesn't exist" and `Unauthorized` for "exists but forbidden" would itself be the distinguishing signal FR-004 forbids. Every persona read/write gated by `PersonaAccessEvaluator` returns **only** `Unauthorized` with one fixed generic message for both cases (research.md D4).
- **`apiKey` structural exclusion (FR-009/FR-010)** cannot be a `[JsonIgnore]` attribute-based strip, because Blazor Interactive Server components never round-trip through JSON — component "State" services (`src/EnterpriseAIPlatform.Web/Services/`) hold direct .NET object references from Application-layer calls, serialized to the browser only as SignalR render-diffs. `PersonaPublicDTO` is therefore a genuinely separate record with no `ApiKey` property at all — the first instance of this two-type secret-split pattern in the codebase (research.md D5).

- **`dataProducts`-required-when-`DataProduct`-extension-selected (FR-011)** follows this codebase's existing static-rules-class convention (`ConversationRenameRules`, `MultiChatQuadrantRules`), not DataAnnotations/FluentValidation (neither is used anywhere in `src/`).
- **Concurrent edit/delete during an in-flight transfer (FR-013, per clarification)** is satisfied by a standard EF Core `RowVersion` optimistic-concurrency token, not a bespoke "transfer in progress" flag — since the transfer is already a single atomic statement (D1), there is no multi-step "in-flight" window to mark; any edit/delete that raced it and lost gets a `DbUpdateConcurrencyException`, mapped to a specific conflict error (research.md D7).
- Hashed owner/collaborator identities reuse spec 002's `IIdentityHasher.ForEmail` — no new hashing utility.
- Sharing-target role-gating (FR-008) is 009's **own**, self-contained binary admin/non-admin rule, exactly as FR-008 states it — per this spec's own Assumptions, consuming spec 018's `SharingDecision` is an explicit future refactor, out of scope here.

## Technical Context

**Language/Version**: C# on **.NET 10** — same solution as specs 002/014/017/018; adds project folders only, no new projects.

**Primary Dependencies**: **Entity Framework Core** + `Microsoft.EntityFrameworkCore.SqlServer` (already a dependency via spec 014's `ModelAccessDbContext`); spec 002's `ICurrentUserAccessor`, `UserModel`/`RoleFlags`, `IIdentityHasher`, `PolicyNames`, `ServerActionResponse<T>`. No new NuGet package.

**Storage**: **Azure SQL Database via EF Core** for `PersonaModel` — a new `PersonaDbContext` (own `Migrations` folder), following `ModelAccessDbContext`'s exact pattern (lazy connection string, `List<string>`/complex fields stored via JSON `ValueConverter` + `ValueComparer`). A `RowVersion` (`Guid`, EF Core `IsConcurrencyToken()`, self-managed rather than SQL Server's native auto-generated `IsRowVersion()` — provider-portable for the InMemory test provider, research.md D7) concurrency token on `PersonaModel` provides the optimistic-concurrency check FR-013 needs.

**Testing**: **xUnit** for `PersonaAccessEvaluator`'s full role × ownership × lesson-persona matrix (SC-002/SC-003) and `PersonaExtensionRules`' conditional-required validation (SC-007), both DI-free per the static-function convention; **`WebApplicationFactory`** integration tests for the full HTTP CRUD+auth vertical (SC-001, SC-004, SC-005, SC-006), extending `TestAuthHandler` to carry `RoleFlags` (currently admin/non-admin only) so employee/contractor/student can be simulated over HTTP, not only at the unit level.

**Target Platform**: Same as specs 002/014/017/018 — Linux containers on Azure, Entra ID.

**Project Type**: Web application — extends the existing layered monolith; adds real HTTP endpoints (unlike spec 018, which was evaluator-only).

**Performance Goals**: Persona read/write adds one indexed SQL round-trip; no caching layer (personas are mutated far more often, relative to reads, than model-access config was — a cache would need invalidation machinery this spec doesn't need yet).

**Constraints**: Every access decision resolved server-side via `PersonaAccessEvaluator` on every entry point (Principle II); `apiKey` structurally absent from any Web-layer-reachable type (Principle II Security Constraints); `dataProducts` conditional-required rule enforced in the one shared `PersonaExtensionRules` class, not per-UI-call-site (Principle V).

**Scale/Scope**: Internal enterprise pilot scope (same as R1). FR-001–FR-013, SC-001–SC-007. Explicitly excludes the AI-assisted persona builder/live-preview (spec 010) and A2A invocation mechanics (spec 011) — this spec owns only the `ApiKey`/`A2aEnabled` fields' existence and exclusion, not their invocation-time use.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluated against constitution v1.2.1.

| Principle / Constraint | Assessment | Verdict |
|---|---|---|
| **I. Azure-Only, No AWS Vestiges** | Azure SQL via EF Core, same as spec 014. No AWS-shaped code. | ✅ PASS |
| **II. Explicit, Server-Side Authorization** | `PersonaAccessEvaluator` is the single, server-side, DI-free decision point for every read/write (FR-003–FR-005); no UI-side gating substitutes for it. `apiKey` structurally excluded at the type level, not by convention (FR-009/FR-010) — directly satisfies the Security & Compliance Constraints clause naming `apiKey` explicitly. | ✅ PASS |
| **III. Fail Loud, Never Fabricate Success** | Rejected operations (unauthorized, lesson-persona-blocked, failed validation, transfer conflict) return a distinct, non-2xx `ServerActionResponse` and leave state unchanged (FR-012) — never a fabricated success. A `DbUpdateConcurrencyException` during transfer surfaces as a specific conflict error, not a silent partial write. | ✅ PASS |
| **IV. One Implementation Per Concern** | One `PersonaAccessEvaluator`, one `PersonaExtensionRules`, one `PersonaPublicDTO` projection — architecture-tested, mirroring spec 014/018's single-implementation tests. | ✅ PASS |
| **V. Schema-Enforced, Not UI-Enforced, Validation** | `PersonaExtensionRules.TryValidate` is called from every create/update entry point (Application/Infrastructure), not only a UI form — a direct API call is bound by the same rule (FR-011). | ✅ PASS |
| **VI. Testable, EARS-Style Requirements** | Spec FRs are already EARS/`MUST` form with a per-story Independent Test; SC-001…SC-007 each map to a concrete xUnit/integration test below. | ✅ PASS |
| **Tech Stack Alignment** | Azure SQL via EF Core for a "strongly relational, schema-stable" entity — matches the constitution's Data & Storage section directly; no Cosmos usage introduced for personas. | ✅ PASS |
| **Security & Compliance Constraints** | `apiKey` excluded structurally at the type/accessor level (constitution's explicit example), not per-call-site convention; caller identity for every ownership/collaborator check comes from `ICurrentUserAccessor`'s server-derived `UserModel`, never client-supplied. | ✅ PASS |

**Result**: No violations. Complexity Tracking is intentionally empty. The spec's own framing (Cosmos partition-key move, delete-then-recreate) describes the *legacy* accelerator's bug, not a target this plan preserves — building on Azure SQL instead is a scope-fit correction consistent with the constitution's already-stated storage guidance, not a new exception.

## Project Structure

### Documentation (this feature)

```text
specs/009-persona-crud-authorization/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md         # Phase 1 output (/speckit-plan command)
├── contracts/            # Phase 1 output (/speckit-plan command)
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

Extends the existing layered solution — no new projects, new folders within the existing four, peer to `ModelAccess`/`Chat`/`Sharing`.

```text
EnterpriseAIPlatform.slnx
src/
├── EnterpriseAIPlatform.Domain/
│   └── Personas/                          # PersonaModel, PersonaPublicDTO, PersonaShareTarget,
│                                           # PersonaShareTargetType enum (Individual/Group)
├── EnterpriseAIPlatform.Application/
│   └── Personas/                          # IPersonaService (contract); PersonaAccessEvaluator
│                                           # (static pure gate, mirrors SharingPolicyEvaluator);
│                                           # PersonaExtensionRules (static, mirrors
│                                           # ConversationRenameRules/MultiChatQuadrantRules)
├── EnterpriseAIPlatform.Infrastructure/
│   ├── Personas/                          # PersonaDbContext (EF Core, Azure SQL) + Migrations/,
│   │                                       # PersonaService : IPersonaService
│   └── DependencyInjection/
│       └── PersonaServiceCollectionExtensions.cs   # AddPersonaInfrastructure(...)
└── EnterpriseAIPlatform.Web/
    └── Endpoints/Personas/                # PersonaEndpoints — minimal-API CRUD + transfer route
tests/
├── EnterpriseAIPlatform.UnitTests/
│   └── Personas/                          # PersonaAccessEvaluatorTests (full role x ownership x
│                                           # lesson matrix), PersonaExtensionRulesTests
├── EnterpriseAIPlatform.IntegrationTests/
│   └── Personas/                          # full CRUD+auth HTTP vertical, ownership-transfer
│                                           # conflict test, apiKey-exclusion scan
└── EnterpriseAIPlatform.ArchitectureTests/
    └── PersonaSingleImplementationTests.cs # exactly one IPersonaService; PersonaPublicDTO has no
                                             # ApiKey property (reflection-checked)
```

**Structure Decision**: Reuse the existing layered monolith — `Personas` is a peer folder to `ModelAccess`/`Chat`/`Sharing`, keeping the "one implementation per concern" boundary at the folder level per Principle IV. Unlike spec 018, this feature needs a real `Web/Endpoints` folder since personas are a user-facing CRUD resource, not an internal evaluator.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No constitution violations — this section is intentionally empty.

## Phase 0 & 1 Artifacts

- [research.md](./research.md) — technology and design decisions (Phase 0)
- [data-model.md](./data-model.md) — entities & validation (Phase 1)
- [contracts/](./contracts/) — service interface, route table, and authorization-policy contracts (Phase 1)
- [quickstart.md](./quickstart.md) — end-to-end validation guide (Phase 1)
