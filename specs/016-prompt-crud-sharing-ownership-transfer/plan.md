# Implementation Plan: Prompt CRUD, Sharing & Ownership Transfer

**Branch**: `016-prompt-crud-sharing-ownership-transfer` | **Date**: 2026-08-31 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/016-prompt-crud-sharing-ownership-transfer/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Like spec 009, this spec is greenfield in code — no `Prompt` type exists anywhere in `src/` — despite narrating "as-is" bugs (`TransferPromptOwnerShip`, `EnsurePromptOperation`, Cosmos delete-then-recreate) from the legacy accelerator. The plan builds the target behaviour directly on this platform's conventions rather than reproducing the legacy mechanics the bug narrative describes.

The dominant fact about this feature is how much of it is **already-proven pattern**. Spec 009 shipped a structurally near-identical resource one commit ago, so the majority of the work is applying a known-good shape to a new entity rather than solving new problems:

- **Storage is Azure SQL via EF Core** (research.md D1), per the constitution's Data & Storage guidance naming prompts explicitly. This collapses US2 almost entirely: FR-007's "atomic or recoverable" transfer, framed in the spec against a Cosmos partition-key move, becomes a **single-row `UPDATE`** in one `SaveChangesAsync()`. `Id` never changes, so zero-or-two-copies is unreachable and FR-007/FR-008 hold by construction.
- **US1's field-injection defence is structural, not validation** (research.md D4). The transfer DTO carries *only* `NewOwnerEmail` — there is no property for `name`/`description`/`createdAt`/`sharedWith` to bind to, so forged values are dropped by the model binder before any handler code runs. SC-001 passes by construction.
- **`PromptAccessEvaluator`** is a new static, pure Application-layer gate (mirroring `PersonaAccessEvaluator` / `SharingPolicyEvaluator` / `ModelAccessEvaluator`) implementing FR-001/FR-002 — write for admin/owner/collaborator, read additionally for share targets, transfer for owner/admin only. As in spec 009, `NotFound` must never be returned from a gated path — `ResponseStatus.NOT_FOUND` and `UNAUTHORIZED` map to different HTTP codes, so the naive "404 when missing, 401 when forbidden" split would itself be the enumeration signal FR-009 forbids (research.md D3).
- **Sharing consumes spec 018's `ISharingPolicyService`** (research.md D5, per clarification). 018 shipped evaluator-only with zero consumers; this spec is its first, retiring the Principle IV risk of a second sharing implementation and giving 018 its first integration coverage.
- **Prompt generation extends spec 014's existing config** with ordered `PrimaryModelId`/`FallbackModelId` (research.md D8) — that entity already declares itself the allow-list for "persona/**prompt** generation". Generation calls the existing streaming `IChatCompletionClient` and accumulates chunks; US3 is then just a structured-JSON error path where the legacy returned plain text.
- **Favorites use a database-level cascade** (research.md D7), making SC-006's "0 dangling references" a schema property rather than something every delete path must remember (Principle V).
- **US6 seeds the composer through spec 024's existing `ChatComposerState`** (research.md D11) — FR-012 is a verbatim assignment with no substitution, so this reuses the whole existing send path rather than adding a second one.

**One cross-spec ripple, flagged deliberately** (research.md D6): FR-002 grants read access via **group tokens**, but `UserModel` carries only role flags — `RoleClaimsTransformation` reads the Entra `groups` claim and discards the GUIDs after deriving roles. Implementing FR-002 in full requires retaining them as `UserModel.GroupTokens`. The change is additive with an empty default and touches spec 002's walking skeleton, so "spec 002's existing tests stay green, unmodified" is an explicit acceptance condition. The documented fallback, if that proves contentious, is individual-only share targets with FR-002's group half deferred.

## Technical Context

**Language/Version**: C# on **.NET 10** — same solution as specs 002/014/017/018/009/024; adds project folders only, no new projects.

**Primary Dependencies**: **Entity Framework Core** + `Microsoft.EntityFrameworkCore.SqlServer` (already present via specs 014/009). Spec 002's `ICurrentUserAccessor`, `UserModel`/`RoleFlags`, `IIdentityHasher`, `PolicyNames`, `ServerActionResponse<T>`; spec 018's `ISharingPolicyService`/`ShareTargetRequest`/`SharingDecision`; spec 014's `PersonaGenerationModelConfig` + `IModelAccessService`; spec 004's `IChatCompletionClient`; spec 024's `ChatComposerState`. **No new NuGet package** (research.md, Constitution currency check).

**Storage**: **Azure SQL Database via EF Core** — a new `PromptDbContext` (own `Migrations/` folder) holding `PromptModel` and `PromptFavorite`, following `PersonaDbContext`'s exact pattern (lazy connection string via `PromptSql:ConnectionString`, JSON `ValueConverter` + `ValueComparer` for list columns). A self-managed `Guid` `RowVersion` marked `IsConcurrencyToken()` — not `IsRowVersion()`, which is not emulated identically by the InMemory test provider (research.md D10). Plus one additive migration on the existing `ModelAccessDbContext` for spec 014's two new config columns.

**Testing**: **xUnit** for `PromptAccessEvaluator`'s role × ownership × collaborator × share-target matrix (SC-004) and the primary/fallback selection logic (SC-003), both DI-free per the static-function convention; **`WebApplicationFactory`** integration tests via a new `PromptWebApplicationFactory` for the HTTP CRUD/transfer/favorites verticals (SC-001, SC-002, SC-005, SC-006, SC-007); **`EnterpriseAIPlatform.ArchitectureTests`** for one-implementation-per-concern guards.

**Target Platform**: Same as prior specs — Linux containers on Azure, Entra ID.

**Project Type**: Web application — extends the existing layered monolith. Unlike specs 002/004/014/017/009, this one ships **user-facing Blazor UI** (US6/FR-012), reusing spec 024's chat surface.

**Performance Goals**: Prompt read/write adds one indexed SQL round-trip; no caching layer (prompts are mutated frequently relative to reads, so a cache would need invalidation machinery this spec doesn't need). Prompt generation inherits spec 004's existing resilience wrapper around the model call.

**Constraints**: Every access decision resolved server-side via `PromptAccessEvaluator` at every entry point (Principle II); share-target validity resolved *only* through spec 018's `ISharingPolicyService` (Principle IV); non-empty `name`/`description` (FR-003) enforced in a shared rules class reachable from every write path, not per-UI-call-site (Principle V); a gated path never returns `NOT_FOUND` (FR-009).

**Scale/Scope**: Internal enterprise pilot scope. FR-001–FR-019, SC-001–SC-008, US1–US6 (full spec, per clarification). Excludes landing-action configuration (spec 021) and any refactor of spec 009's persona sharing onto `ISharingPolicyService`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluated against constitution v1.2.1.

| Principle / Constraint | Assessment | Verdict |
|---|---|---|
| **I. Azure-Only, No AWS Vestiges** | Azure SQL via EF Core, Entra ID, existing Foundry model seam. No AWS-shaped code introduced. | ✅ PASS |
| **II. Explicit, Server-Side Authorization** | `PromptAccessEvaluator` is the single server-side decision point for every read/write (FR-001/FR-002), applied before any mutation. Transfer authorizes the caller before any write (FR-006). Caller identity always comes from `ICurrentUserAccessor`'s server-derived `UserModel`, never request data — and FR-005's field re-derivation is enforced structurally by the DTO shape (research.md D4). | ✅ PASS |
| **III. Fail Loud, Never Fabricate Success** | Total generation failure returns a structured JSON error, never a fabricated success or a content-type-mismatched body (FR-011). A concurrency conflict surfaces as a distinct 409, not a silent last-write-wins. A failed transfer reports failure and leaves the original intact (FR-007). | ✅ PASS |
| **IV. One Implementation Per Concern** | One `PromptAccessEvaluator`, one `PromptValidationRules`, one `IPromptService`. Sharing delegates to 018's single `ISharingPolicyService` rather than adding a second rule set (research.md D5). Generation extends 014's existing config rather than adding a parallel one (D8). Chat seeding reuses 024's `ChatComposerState` rather than a second send path (D11). All architecture-tested. | ✅ PASS |
| **V. Schema-Enforced, Not UI-Enforced, Validation** | FR-003's non-empty `name`/`description` lives in `PromptValidationRules`, called from the Application layer, so a direct API caller is bound identically to a UI user. FR-017's favorites cleanup is a database cascade, not per-call-site logic (D7). | ✅ PASS |
| **VI. Testable, EARS-Style Requirements** | Spec FRs are `MUST`-form with a per-story Independent Test. SC-001…SC-008 each map to a named test in [quickstart.md](./quickstart.md). FR-010 was sharpened during clarification precisely because an unordered allow-list left "which model is primary" unfalsifiable. | ✅ PASS |
| **Tech Stack Alignment** | Azure SQL via EF Core for a "strongly relational, schema-stable" entity — the constitution names prompts explicitly. Blazor Interactive Server for the UI, the documented default. No new package, so the standing version-pinning follow-up is not triggered. | ✅ PASS |
| **Security & Compliance Constraints** | No credential field exists on `PromptModel`, so the `apiKey`-style structural-exclusion concern does not arise here. Identity for every ownership/collaborator/share decision is server-derived. Group tokens (research.md D6) come from the verified Entra `groups` claim, never client-supplied. | ✅ PASS |

**Result**: No violations. Complexity Tracking is intentionally empty.

Two items are recorded here as *deliberate, in-bounds* decisions rather than exceptions. First, the spec's Cosmos framing describes the **legacy** accelerator's defect, not a target this plan preserves — building on Azure SQL is a scope-fit correction consistent with already-stated constitutional guidance, exactly as spec 009 resolved it. Second, extending `UserModel` (research.md D6) modifies a spec 002 type; this is additive with a safe default and is the Principle IV-preferred alternative to introducing a second source of truth for caller identity, but it carries the ripple risk called out in the Summary.

## Project Structure

### Documentation (this feature)

```text
specs/016-prompt-crud-sharing-ownership-transfer/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

Extends the existing layered solution — no new projects; new `Prompts` folders peer to `Personas`/`ModelAccess`/`Chat`/`Sharing`.

```text
EnterpriseAIPlatform.slnx
src/
├── EnterpriseAIPlatform.Domain/
│   ├── Prompts/                           # PromptModel, PromptFavorite, PromptShareTarget,
│   │                                       # PromptPublicDTO, PromptOperation
│   └── ModelAccess/
│       └── PersonaGenerationModelConfig.cs # MODIFIED (014): + PrimaryModelId, FallbackModelId
├── EnterpriseAIPlatform.Application/
│   ├── Prompts/                           # IPromptService, IPromptGenerationService (contracts);
│   │                                       # PromptAccessEvaluator (static pure gate);
│   │                                       # PromptValidationRules (static, FR-003)
│   └── Identity/
│       └── UserModel.cs                   # MODIFIED (002): + GroupTokens (research.md D6)
├── EnterpriseAIPlatform.Infrastructure/
│   ├── Prompts/                           # PromptDbContext (EF Core, Azure SQL) + Migrations/,
│   │                                       # PromptService : IPromptService,
│   │                                       # PromptGenerationService : IPromptGenerationService
│   ├── ModelAccess/Migrations/            # MODIFIED (014): additive migration for the two columns
│   ├── Authentication/
│   │   └── RoleClaimsTransformation.cs    # MODIFIED (002): retain groups claim on UserModel
│   └── DependencyInjection/
│       └── PromptServiceCollectionExtensions.cs   # AddPromptInfrastructure(...)
└── EnterpriseAIPlatform.Web/
    ├── Endpoints/Prompts/                 # PromptEndpoints — CRUD, transfer, favorites, generator
    ├── Components/Prompts/                # PromptLibrary.razor (list + select-to-chat, US6)
    ├── Components/Chat/SidebarNav.razor   # MODIFIED (024): + Prompts nav link
    └── Services/
        └── ChatComposerState.cs           # MODIFIED (024): + SeedFromPrompt(...) (research.md D11)
tests/
├── EnterpriseAIPlatform.UnitTests/
│   └── Prompts/                           # PromptAccessEvaluatorTests (role x ownership x
│                                           # collaborator x share-target matrix),
│                                           # PromptValidationRulesTests,
│                                           # PromptGenerationFallbackTests
├── EnterpriseAIPlatform.IntegrationTests/
│   ├── PromptWebApplicationFactory.cs     # isolated InMemory provider (research.md D9)
│   └── Prompts/                           # CRUD+authz vertical, forged-field transfer corpus,
│                                           # concurrency 409, favorites cascade, generator JSON error
└── EnterpriseAIPlatform.ArchitectureTests/
    └── PromptSingleImplementationTests.cs  # exactly one IPromptService; no prompt-local sharing
                                             # rule (PromptService depends on ISharingPolicyService)
```

**Structure Decision**: Reuse the existing layered monolith — `Prompts` is a peer folder to `Personas`/`ModelAccess`/`Chat`/`Sharing`, keeping the Principle IV boundary visible at folder level. This is the first spec to modify types owned by earlier specs (002's `UserModel`, 014's config entity, 024's `ChatComposerState`/`SidebarNav`); each modification is additive and is called out explicitly above so `/speckit-tasks` sequences the cross-spec edits before the code that depends on them.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No constitution violations — this section is intentionally empty.

## Phase 0 & 1 Artifacts

- [research.md](./research.md) — technology and design decisions, D1–D11 (Phase 0)
- [data-model.md](./data-model.md) — entities, relationships & validation (Phase 1)
- [contracts/](./contracts/) — service interfaces, route table, and authorization-policy contracts (Phase 1)
- [quickstart.md](./quickstart.md) — end-to-end validation guide, SC-001–SC-008 (Phase 1)
