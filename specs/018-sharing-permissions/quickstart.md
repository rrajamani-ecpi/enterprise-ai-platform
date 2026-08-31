# Quickstart: Validating Sharing & Permissions Policy

**Feature**: 018-sharing-permissions | **Date**: 2026-08-31

This is a **validation/run guide** for an evaluator-only feature with no HTTP surface (research.md D8) — there is no route to `curl`. Validation is exercising `ISharingPolicyService`/`SharingPolicyEvaluator` directly, either via the automated test suite or an interactive scratch harness. Implementation steps belong to `tasks.md` (produced by `/speckit-tasks`).

## Prerequisites

- Spec 002's walking skeleton (Entra sign-in, `UserModel`/`RoleFlags`, `RoleName`) — this feature reuses it directly for caller identity; no new session/auth work.
- A configured `RoleSharing` and `GlobalSharingOverride` section in `appsettings.Development.json` (see [contracts/config-schema.md](./contracts/config-schema.md)) — the app fails to start without a valid `RoleSharing:Roles` entry for every non-admin `RoleName`.

## Setup

```bash
dotnet restore
dotnet build EnterpriseAIPlatform.sln
# populate RoleSharing and GlobalSharingOverride sections per contracts/config-schema.md
dotnet run --project src/EnterpriseAIPlatform.Web   # confirms startup validation passes
```

## Validation scenarios (one per user story)

Since there is no HTTP route or UI yet (no consumer of `ISharingPolicyService` is implemented — 009/012/016 are out of scope for this feature), each scenario is exercised by constructing a `UserModel`/`RoleFlags` and a `ShareTargetRequest` and calling `SharingPolicyEvaluator.Evaluate` directly — either in a throwaway console/REPL snippet or, preferably, by pointing at the corresponding `[Fact]`/`[Theory]` in `SharingPolicyEvaluatorTests`.

| Story | Steps | Expected (pass) |
|---|---|---|
| **1 · Base share targets** (P1) | Evaluate with an `Employee` caller against an `Individual` request, and against a `Group` request for a group in that role's `AllowedGroups`. | Both `Allow` (SC-002 baseline); a `Group` request for a group *not* in `AllowedGroups` (with `GroupSharingEnabled=false` or the group absent) → `Deny(RolePolicyDenied)`. |
| **2 · Role-based policy matrix** (P1) | Evaluate the full matrix: `{Admin, Employee, Contractor, Student}` × `{Individual, each configured group}`, with no global override active. | 100% of outcomes match `contracts/config-schema.md`'s configured policy (SC-002) — `Admin` always `Allow`; `Employee`/`Student` individual always `Allow`; group outcomes depend on `GroupSharingEnabled`/`AllowedGroups`; `Contractor` (not named in spec, but present in `RoleName`) follows its own configured entry with no special-casing. |
| **3 · Global overrides** (P2) | Set `DisableAllGroupSharing=true`; evaluate a non-admin `Group` request (expect `Deny`) and a non-admin `Individual` request (expect `Allow`). Reset; set `AdminOnlyMode=true`; evaluate a non-admin `Individual` and `Group` request (expect `Deny` for both) and an `Admin` request of each type (expect `Allow`). Reset both; add a group to `GloballyAllowedGroups` that a `Student` caller's `AllowedGroups` does not contain; evaluate a `Student` `Group` request for it. | `DisableAllGroupSharing`: group denied, individual unaffected (SC-003). `AdminOnlyMode`: all non-admin denied, admin unaffected (SC-004). `GloballyAllowedGroups`: `Allow(GloballyAllowedGroup)` even though the role's own policy would have denied it (SC-005) — unless `DisableAllGroupSharing`/`AdminOnlyMode` is simultaneously active, in which case those take precedence (evaluate that combination too). |
| **4 · Read vs. collaborator access level** (P2) | Construct a `ShareTarget` with default `AccessLevel` and one with `AccessLevel.Collaborator` explicitly set. | Default is `Read` (SC-006) — this is a data-shape assertion (data-model.md's `ShareTarget`), not a call into the evaluator, since access-level enforcement itself belongs to each resource type's own authorization check (out of scope here). |
| **Multi-role resolution** (Edge Case) | Evaluate with `RoleFlags(IsAdmin: false, IsEmployee: true, IsContractor: false, IsStudent: true)` against a `Group` request allowed for `Student` but not for `Employee` (or vice versa). | `Allow` — most-permissive-applicable-role wins (research.md D4), regardless of which flag "would have" denied it alone. |
| **Startup fail-loud** (Constitution III) | Remove the `Student` key from `RoleSharing:Roles`, or add an `Admin` key to it, and start the app. | Application fails to start with a validation error — never falls back to a permissive/restrictive default silently (contracts/config-schema.md). |

## Automated test commands

```bash
dotnet test tests/EnterpriseAIPlatform.UnitTests --filter FullyQualifiedName~Sharing   # full role x target x override matrix (SC-002..SC-005)
dotnet test tests/EnterpriseAIPlatform.ArchitectureTests --filter FullyQualifiedName~Sharing   # exactly one ISharingPolicyService (Principle IV)
```

## Done when

- [ ] All six scenarios above pass via `SharingPolicyEvaluatorTests`.
- [ ] Unit test project is green, covering SC-001 through SC-007.
- [ ] Architecture test confirms exactly one `ISharingPolicyService` implementation.
- [ ] Startup fails on a malformed/incomplete `RoleSharing`/`GlobalSharingOverride` section (manually verified once — this is a startup-path check, not a unit test).
- [ ] No HTTP route, database migration, or admin UI was added — this feature is evaluator-only (SC-001..SC-007 fully covered without one).
