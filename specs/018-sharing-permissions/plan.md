# Implementation Plan: Sharing & Permissions Policy

**Branch**: `018-sharing-permissions` | **Date**: 2026-08-31 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/018-sharing-permissions/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

This spec introduces the canonical, resource-agnostic **sharing-policy evaluator** — the single `SharingDecision` computation that specs 009 (personas), 012 (data products), and 016 (prompts) should each eventually consume instead of re-implementing their own share-target validity rules (constitution Principle IV). Per the spec's own Clarifications/Assumptions, this is deliberately narrow: **no new persisted data store, no admin CRUD/API** — `RoleSharingPolicy` and `GlobalSharingOverride` are both read from static, deployment-level `IOptions`-bound configuration, and the evaluator is a pure, stateless function over that config plus the caller's already-derived role flags and the requested share target.

- The role dimension in `RoleSharingPolicy` is keyed off the **already-implemented** `RoleName`/`RoleFlags` model from spec 002 (`Admin`, `Employee`, `Contractor`, `Student` — no `Faculty` flag exists in code). The PRD's "faculty" language maps to `Employee`, consistent with spec 012's existing `@employees`/`@contractors` group tokens (FR-004/FR-005, mapped onto real roles rather than the spec's illustrative names).
- Individual-target sharing is always permitted for non-admin roles (FR-004/FR-005) and is not itself config-driven; only group-target eligibility (`GroupSharingEnabled`, `AllowedGroups`) is read from `RoleSharingPolicyOptions`. Admin's "share with everything" behavior (FR-003) is a hardcoded evaluator rule, not a configurable entry — it MUST NOT be overridable via config.
- Global overrides (`DisableAllGroupSharing`, `AdminOnlyMode`, `GloballyAllowedGroups`) are read from a second options class, `GlobalSharingOverrideOptions`, and evaluated ahead of per-role policy per FR-010's precedence rule.
- A caller with multiple roles is evaluated by the most-permissive-applicable-role rule (spec Edge Cases): the decision is allow if *any* of the caller's roles would allow it, after global overrides are applied.
- No new HTTP routes or Web-layer surface: 018 exposes only an internal service contract (`ISharingPolicyService`) for future consumers (009/012/016) to call. Refactoring those specs onto this contract is explicitly out of scope here (spec Assumptions).
- Mirrors spec 014's `ModelAccessEvaluator` shape exactly: a static, pure `SharingPolicyEvaluator.Evaluate(...)` function in Application (no DI, fully unit-testable without any infrastructure), wrapped by a thin `SharingPolicyService : ISharingPolicyService` in Infrastructure that resolves the current `IOptionsSnapshot<T>` values and calls the pure function.

## Technical Context

**Language/Version**: C# on **.NET 10** — same solution as specs 002/014; this feature adds project folders only, no new projects.

**Primary Dependencies**: `Microsoft.Extensions.Options` (`IOptionsSnapshot<T>` binding + `ValidateDataAnnotations().ValidateOnStart()`, matching `RoleDerivationMappingOptions`'s pattern) and spec 002's `UserModel`/`RoleFlags`/`RoleName`. No EF Core, no Redis, no external SDK — there is no I/O on the decision path.

**Storage**: **N/A.** Per the spec's own resolved clarification, `RoleSharingPolicy` and `GlobalSharingOverride` are static `appsettings`-bound configuration only; this feature introduces no database, cache, or admin mutation API. Changing either is an ops/config-deployment action (spec Assumptions), not an in-app one.

**Testing**: **xUnit** unit tests over the pure `SharingPolicyEvaluator.Evaluate` function, covering the full role × target-type × override combinatorial matrix (mirrors SC-002/SC-003/SC-004/SC-005 and `ModelAccessEvaluatorTests`'s `Condition_State_Outcome` naming convention) in `tests/EnterpriseAIPlatform.UnitTests`; an `EnterpriseAIPlatform.ArchitectureTests` check enforcing exactly one `ISharingPolicyService` implementation (Principle IV, mirroring `ModelAccessSingleImplementationTests`).

**Target Platform**: Same as specs 002/014 — Linux containers on Azure, Entra ID. No new infrastructure.

**Project Type**: Web application — extends the existing layered monolith (Domain/Application/Infrastructure/Web); no new top-level project.

**Performance Goals**: Sub-millisecond, in-memory decision (no I/O on the hot path) — negligible overhead per share-request evaluation.

**Constraints**: Every decision resolved server-side on every entry point (Principle II, FR-015); client-supplied validity signals are never trusted (FR-006 requires identical UI/API outcomes, which follows automatically from there being exactly one server-side evaluator). Config validation happens at startup via DataAnnotations, not deferred to a future admin UI (Principle V) — matches `RoleDerivationMappingOptions`'s existing pattern.

**Scale/Scope**: Small, fixed-shape config (one `RoleSharingPolicyOptions` entry per non-admin `RoleName`, one `GlobalSharingOverrideOptions` singleton). This spec's consumers (009/012/016) are not yet implemented in `src/` — 018 ships as a standalone, fully-tested evaluator with no live caller yet, same posture as 014's provider-adapter seam ahead of R2.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluated against constitution v1.2.1.

| Principle / Constraint | Assessment | Verdict |
|---|---|---|
| **I. Azure-Only, No AWS Vestiges** | No cloud resource of any kind — pure in-memory config evaluation. Nothing to violate. | ✅ PASS |
| **II. Explicit, Server-Side Authorization** | This feature *is* the server-side authorization mechanism for sharing (FR-002, FR-015); FR-006 requires identical UI/API outcomes, satisfied structurally by having exactly one evaluator with no client-trusted input. | ✅ PASS |
| **III. Fail Loud, Never Fabricate Success** | Malformed/missing config fails the app at startup (`ValidateOnStart`), never silently defaults to an unsafe "allow" — there is no fail-open carve-out claimed here (unlike 014's cache fallback), since a wrong sharing decision is a security defect, not an availability one. | ✅ PASS |
| **IV. One Implementation Per Concern** | Single `ISharingPolicyService`/`SharingPolicyEvaluator`, enforced by an architecture test; this spec exists specifically so 009/012/016 stop each reimplementing their own share-validity check. | ✅ PASS |
| **V. Schema-Enforced, Not UI-Enforced, Validation** | `RoleSharingPolicyOptions`/`GlobalSharingOverrideOptions` are validated via DataAnnotations at startup, independent of any UI; there is no UI for this feature at all (config is ops-managed), so no UI-only validation path can exist to bypass. | ✅ PASS |
| **VI. Testable, EARS-Style Requirements** | Spec FRs are already EARS/`MUST` form with a per-story Independent Test; each SC-001…SC-007 maps to a concrete xUnit case in the evaluator's combinatorial matrix. | ✅ PASS |
| **Tech Stack Alignment** | Extends the existing .NET 10 layered monolith with an `IOptions`-bound config seam, the same pattern already used by `RoleDerivationMappingOptions`/`ContentSafetyOptions` — no new stack element introduced. | ✅ PASS |
| **Security & Compliance Constraints** | Caller role/identity MUST be the already-derived server-side `UserModel`/`RoleFlags` (spec 002), never client-supplied (FR-015); this feature has no credential/secret surface to exclude. | ✅ PASS |

**Result**: No violations. Complexity Tracking is intentionally empty. The absence of a `Faculty` role flag in the implemented identity model (spec 002) is a scope-fit note, not a constitution exception — 018's policy keys off the real `RoleName` enum (`Admin`/`Employee`/`Contractor`/`Student`), with the PRD's "faculty" examples mapped onto `Employee` (see research.md D2).

## Project Structure

### Documentation (this feature)

```text
specs/018-sharing-permissions/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md         # Phase 1 output (/speckit-plan command)
├── contracts/            # Phase 1 output (/speckit-plan command)
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

Extends the existing layered solution — no new projects, new folders within the existing four, peer to `ModelAccess`/`Identity`/`Authentication`.

```text
EnterpriseAIPlatform.sln
src/
├── EnterpriseAIPlatform.Domain/
│   └── Sharing/                          # ShareTargetType, AccessLevel enums; ShareTarget, SharingDecision
├── EnterpriseAIPlatform.Application/
│   └── Sharing/                          # ISharingPolicyService (contract); SharingPolicyEvaluator
│                                          # (static pure function, mirrors ModelAccessEvaluator)
└── EnterpriseAIPlatform.Infrastructure/
    ├── Sharing/                           # RoleSharingPolicyOptions, GlobalSharingOverrideOptions
    │                                      # (IOptions-bound, DataAnnotations-validated); SharingPolicyService
    └── DependencyInjection/
        └── SharingServiceCollectionExtensions.cs   # AddSharingInfrastructure(...)
tests/
├── EnterpriseAIPlatform.UnitTests/
│   └── Sharing/                          # SharingPolicyEvaluatorTests — full role x target x override matrix
└── EnterpriseAIPlatform.ArchitectureTests/
    └── SharingSingleImplementationTests.cs      # exactly one ISharingPolicyService (Principle IV)
```

**Structure Decision**: Reuse the existing layered monolith — this feature is a shared cross-cutting service with no persistence and no HTTP surface, so it needs no `Web/Endpoints/Sharing` folder (unlike 014's `Endpoints/ModelAccess`). `Sharing` is a peer folder to `ModelAccess`/`Identity`/`Authentication`, keeping the "one implementation per concern" boundary at the folder level per Principle IV.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No constitution violations — this section is intentionally empty.

## Phase 0 & 1 Artifacts

- [research.md](./research.md) — technology and design decisions (Phase 0)
- [data-model.md](./data-model.md) — entities & validation (Phase 1)
- [contracts/](./contracts/) — service interface and config schema contracts (Phase 1)
- [quickstart.md](./quickstart.md) — end-to-end validation guide (Phase 1)
