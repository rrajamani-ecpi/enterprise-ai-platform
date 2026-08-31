# Tasks: Sharing & Permissions Policy

**Input**: Design documents from `/specs/018-sharing-permissions/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)
**Tests**: Included — this spec's success criteria (SC-001…SC-007) are defined as test suites, and Constitution Principle VI requires falsifiable per-story tests.

**Scope note** (per `plan.md`'s Summary and `research.md` D1/D8): this feature is the **evaluator only** — `ISharingPolicyService`/`SharingPolicyEvaluator`, its two `IOptions`-bound config classes, and the `ShareTarget`/`SharingDecision` vocabulary. **Explicitly deferred — no tasks generated here, not forgotten**:
- Any actual persona/prompt/data-product sharing behavior (grant, revoke, group-membership resolution against a real resource) — that belongs to specs 009/012/016's own future refactor onto this policy, which is out of scope for 018 per its Assumptions.
- **SC-006** (0 edit operations succeed for a read-only recipient "across a test corpus spanning personas, prompts, and data products"), **SC-007**/**FR-011** (previously-granted shares remain functional after an override activates — no implicit revocation), and **FR-014** (collaborator status must not grant owner-only actions) — all require a real persisted resource/grant, which does not exist in this feature or in 009/012/016's current (spec-only) state. Untestable/unbuildable until a consuming spec is implemented; tracked here as explicitly deferred, not silently dropped.
- **Contractor's sharing policy** (`RoleName.Contractor`, spec Assumptions addendum) — spec 018 defines no FR for it; the evaluator applies a fail-safe restrictive default (T007) rather than a guessed value, pending a future `/speckit.clarify` pass.

## Path Conventions (from plan.md — extends the existing layered solution, no new project)

- `src/EnterpriseAIPlatform.Domain/Sharing/`, `.Application/Sharing/`, `.Infrastructure/Sharing/`, `.Infrastructure/DependencyInjection/`
- `tests/EnterpriseAIPlatform.UnitTests/Sharing/`, `.ArchitectureTests/`

**Implementation note**: `RoleSharingPolicyOptions`/`RolePolicy` and `GlobalSharingOverrideOptions` (T007/T008) were implemented in `src/EnterpriseAIPlatform.Application/Sharing/`, not `.Infrastructure/Sharing/` as plan.md's Project Structure originally stated. `RoleSharingPolicyOptions` is keyed by `RoleName`, which lives in `Application.Authorization`, and is consumed directly by the pure `SharingPolicyEvaluator` (also Application) — placing the options types in Infrastructure would create a reverse Application→Infrastructure dependency, which the solution's layering (Domain → Application → Infrastructure → Web) forbids. `SharingPolicyService` (the `IOptionsSnapshot<T>`-resolving, DI-registered implementation) and the DI registration itself remain in Infrastructure, as planned.

---

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 Create empty `Sharing` folders in `src/EnterpriseAIPlatform.Domain/Sharing/`, `src/EnterpriseAIPlatform.Application/Sharing/`, and `src/EnterpriseAIPlatform.Infrastructure/Sharing/` per plan.md's Project Structure
- [X] T002 [P] Add placeholder `RoleSharing` and `GlobalSharingOverride` sections (per [contracts/config-schema.md](./contracts/config-schema.md)) to `appsettings.json` / `appsettings.Development.json` in `src/EnterpriseAIPlatform.Web/`

**Checkpoint**: Solution still builds; no new NuGet dependency required (`Microsoft.Extensions.Options` is already available via the ASP.NET Core shared framework, same as `RoleDerivationMappingOptions`).

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ No user story work begins until this phase is complete.**

- [X] T003 [P] Create `ShareTargetType` and `AccessLevel` enums in `src/EnterpriseAIPlatform.Domain/Sharing/ShareTargetType.cs` and `AccessLevel.cs`
- [X] T004 [P] Create `SharingDecisionReason` enum (`AdminOnlyModeActive`, `AdminBypass`, `GroupSharingDisabledGlobally`, `GloballyAllowedGroup`, `RolePolicyAllowed`, `RolePolicyDenied`) and the `SharingDecision` record in `src/EnterpriseAIPlatform.Domain/Sharing/SharingDecision.cs`
- [X] T005 [P] Create the `ShareTargetRequest` record (evaluator input: `Type`, `GroupToken`) in `src/EnterpriseAIPlatform.Domain/Sharing/ShareTargetRequest.cs`
- [X] T006 Declare the `ISharingPolicyService` contract in `src/EnterpriseAIPlatform.Application/Sharing/ISharingPolicyService.cs`
- [X] T007 [P] Create `RoleSharingPolicyOptions`/`RolePolicy` (`IOptions`-bound, `SectionName = "RoleSharing"`, keyed by `RoleName`, with `RolePolicy.Default = { GroupSharingEnabled: false, AllowedGroups: [] }` for any role absent from config — data-model.md fail-safe default) in `src/EnterpriseAIPlatform.Application/Sharing/RoleSharingPolicyOptions.cs` (relocated from `.Infrastructure/` — see Implementation note above)
- [X] T008 [P] Create `GlobalSharingOverrideOptions` (`IOptions`-bound, `SectionName = "GlobalSharingOverride"`) in `src/EnterpriseAIPlatform.Application/Sharing/GlobalSharingOverrideOptions.cs` (relocated from `.Infrastructure/` — see Implementation note above)
- [X] T009 Create `SharingServiceCollectionExtensions.AddSharingInfrastructure` registering both options with `.ValidateDataAnnotations().ValidateOnStart()` (no service registration yet — added in US2) in `src/EnterpriseAIPlatform.Infrastructure/DependencyInjection/SharingServiceCollectionExtensions.cs`

**Checkpoint**: Domain vocabulary and config classes exist and compile; app still starts (options have no consumer yet).

---

## Phase 3: User Story 1 — Share an owned resource with an individual or a group (Priority: P1) 🎯 MVP (part 1/2)

**Goal**: The `ShareTarget`/`ShareTargetRequest` vocabulary correctly distinguishes individual vs. group targets, and the evaluator's baseline (no-override) behavior allows individual targets unconditionally and group targets per role policy — the foundation US2's full policy and every future resource-type integration builds on.

**Independent Test** (narrowed to this feature's evaluator boundary — see Scope note above for what's deferred): construct an `Individual` and a `Group` `ShareTargetRequest` and confirm the evaluator's baseline decision matches the target-type rule, without any override active.

- [X] T010 [P] [US1] Unit test: `ShareTarget` enforces exactly one of `Identity`/`GroupToken` set, matching `Type` (data-model.md invariant) in `tests/EnterpriseAIPlatform.UnitTests/Sharing/ShareTargetTests.cs`
- [X] T011 [US1] Implement `ShareTarget` record with the invariant enforced at construction in `src/EnterpriseAIPlatform.Domain/Sharing/ShareTarget.cs`
- [X] T012 [P] [US1] Unit test: `SharingPolicyEvaluator.Evaluate` allows an `Individual`-target request for every non-admin role with no override active (FR-004/FR-005 baseline) in `tests/EnterpriseAIPlatform.UnitTests/Sharing/SharingPolicyEvaluatorTests.cs`
- [X] T013 [P] [US1] Unit test: `SharingPolicyEvaluator.Evaluate` allows a `Group`-target request when the group is in the caller's role's `AllowedGroups` and `GroupSharingEnabled` is true, and denies (`RolePolicyDenied`) otherwise, in the same test file
- [X] T014 [US1] Implement `SharingPolicyEvaluator.Evaluate`'s `Individual`-target branch and the `RolePolicyAllowed`/`RolePolicyDenied` `Group`-target branch in `src/EnterpriseAIPlatform.Application/Sharing/SharingPolicyEvaluator.cs`

**Checkpoint**: US1's vocabulary and baseline evaluator behavior are independently testable. The resource-level half of US1's Acceptance Scenarios (an actual grantee gaining/losing access on a real persona/prompt/data product) is owned by 009/012/016's future adoption of `ISharingPolicyService` — not built here.

---

## Phase 4: User Story 2 — Configurable, role-based sharing policy constrains valid share targets (Priority: P1) 🎯 MVP (part 2/2)

**Goal**: Every role's share-target validity (admin/employee/contractor/student) is enforced server-side, identically regardless of caller surface, exactly matching the configured `RoleSharingPolicyOptions`.

**Independent Test**: evaluate the full `{Admin, Employee, Contractor, Student}` × `{Individual, each configured group}` matrix directly against `ISharingPolicyService` and confirm every outcome matches the configured policy (SC-002).

- [X] T015 [P] [US2] Unit test: `Admin` caller is allowed for any `Individual`/`Group` request regardless of `RoleSharingPolicyOptions` content (FR-003, `AdminBypass` reason) in `tests/EnterpriseAIPlatform.UnitTests/Sharing/SharingPolicyEvaluatorTests.cs`
- [X] T016 [P] [US2] Unit test: full combinatorial matrix — `{Employee, Contractor, Student}` × `{Individual, each configured group}` — matches `RoleSharingPolicyOptions` exactly (SC-002) in the same test file
- [X] T017 [P] [US2] Unit test: `RoleSharingPolicyOptions` validation rejects a config containing an `Admin` key; a config that simply omits a non-admin `RoleName` (e.g. `Contractor`) passes validation and the evaluator falls back to `RolePolicy.Default` for that role (data-model.md fail-safe default, not a validation failure) in `tests/EnterpriseAIPlatform.UnitTests/Sharing/RoleSharingPolicyOptionsTests.cs`
- [X] T018 [US2] Implement the hardcoded `Admin`-bypass rule in `SharingPolicyEvaluator.Evaluate` (research.md D3) in `src/EnterpriseAIPlatform.Application/Sharing/SharingPolicyEvaluator.cs`
- [X] T019 [US2] Implement `RoleSharingPolicyOptions` custom validation (`IValidatableObject`) rejecting an `Admin` key only — non-admin roles are optional, defaulting to `RolePolicy.Default` when absent — in `src/EnterpriseAIPlatform.Application/Sharing/RoleSharingPolicyOptions.cs` (relocated from `.Infrastructure/`)
- [X] T020 [US2] Implement `SharingPolicyService : ISharingPolicyService`, resolving `IOptionsSnapshot<RoleSharingPolicyOptions>` and the caller's `UserModel.Roles`, delegating to `SharingPolicyEvaluator.Evaluate` (FR-002/FR-015) in `src/EnterpriseAIPlatform.Infrastructure/Sharing/SharingPolicyService.cs`
- [X] T021 [US2] Register `ISharingPolicyService` in `AddSharingInfrastructure` in `src/EnterpriseAIPlatform.Infrastructure/DependencyInjection/SharingServiceCollectionExtensions.cs`
- [X] T022 [P] [US2] Architecture test: exactly one `ISharingPolicyService` implementation is registered (Principle IV) in `tests/EnterpriseAIPlatform.ArchitectureTests/SharingSingleImplementationTests.cs`

**Checkpoint**: US1 + US2 complete — the MVP. FR-006's "identical UI/API outcome" is structurally satisfied (T022) since `ISharingPolicyService` is the only decision path (SC-001).

---

## Phase 5: User Story 3 — Global sharing overrides for emergency/administrative control (Priority: P2)

**Goal**: `DisableAllGroupSharing`, `AdminOnlyMode`, and `GloballyAllowedGroups` behave per the FR-007–FR-011 precedence rules.

**Independent Test**: with each override active in turn, representative share requests from multiple roles confirm the override's effect and precedence over per-role policy (SC-003/SC-004/SC-005).

- [X] T023 [P] [US3] Unit test: `DisableAllGroupSharing` denies non-admin `Group` requests, leaves `Individual` requests and admin requests unaffected (SC-003) in `tests/EnterpriseAIPlatform.UnitTests/Sharing/SharingPolicyEvaluatorTests.cs`
- [X] T024 [P] [US3] Unit test: `AdminOnlyMode` denies all non-admin requests (`Individual` and `Group`), admin unaffected (SC-004) in the same test file
- [X] T025 [P] [US3] Unit test: a group in `GloballyAllowedGroups` is allowed even when the caller's own role policy would deny it (additive, SC-005), *unless* `DisableAllGroupSharing`/`AdminOnlyMode` is simultaneously active, in which case those take precedence (FR-010) — in the same test file
- [X] T026 [P] [US3] Unit test: deactivating an override restores per-role policy with no residual effect (Edge Cases) in the same test file
- [X] T027 [US3] Implement the full ordered precedence sequence (research.md D5: `AdminOnlyMode` → `Admin` bypass → `DisableAllGroupSharing` → `GloballyAllowedGroups` → per-role policy) in `SharingPolicyEvaluator.Evaluate` in `src/EnterpriseAIPlatform.Application/Sharing/SharingPolicyEvaluator.cs`
- [X] T028 [US3] Wire `IOptionsSnapshot<GlobalSharingOverrideOptions>` into `SharingPolicyService.Evaluate` in `src/EnterpriseAIPlatform.Infrastructure/Sharing/SharingPolicyService.cs`

**Checkpoint**: US1 + US2 + US3 independently testable together.

---

## Phase 6: User Story 4 — Shared resources default to read access; edit requires explicit collaborator designation (Priority: P2)

**Goal**: `ShareTarget.AccessLevel` defaults to `Read`; `Collaborator` is only ever set by explicit designation.

**Independent Test** (narrowed — see Scope note: actual edit-rejection against a real resource is deferred): construct a `ShareTarget` with no explicit access level and one with `Collaborator` explicitly set, and confirm the default/explicit split.

- [X] T029 [P] [US4] Unit test: a `ShareTarget` constructed without an explicit `AccessLevel` defaults to `Read` (FR-012) in `tests/EnterpriseAIPlatform.UnitTests/Sharing/ShareTargetTests.cs`
- [X] T030 [P] [US4] Unit test: a `ShareTarget` constructed with `AccessLevel.Collaborator` retains it explicitly, with no implicit derivation from `Read` (FR-013) in the same test file
- [X] T031 [US4] Implement the `AccessLevel` default (`= AccessLevel.Read`) on `ShareTarget`'s primary constructor in `src/EnterpriseAIPlatform.Domain/Sharing/ShareTarget.cs`

**Checkpoint**: All four user stories independently testable. FR-014 (collaborator ≠ owner-rights) and real read/edit enforcement remain 009/012/016's responsibility once they adopt `SharingDecision` — not built here.

---

## Phase 7: Polish & Cross-Cutting

- [X] T032 [P] Run every scenario in [quickstart.md](./quickstart.md) end-to-end (evaluator calls + the startup fail-loud check) and fix any gap found — all six scenarios are exercised by `SharingPolicyEvaluatorTests`/`ShareTargetTests`/`SharingServiceCollectionExtensionsTests`, 36/36 passing
- [X] T033 [P] Integration/manual check: application fails to start when `RoleSharing:Roles` contains an `Admin` key (Principle III) — implemented as an automated test (`SharingServiceCollectionExtensionsTests.AdminKeyInConfig_ThrowsOnFirstAccess`) rather than a manual step, consistent with this repo's automated-test-only convention; a missing non-admin `RoleName` entry is a valid, fail-safe-defaulted config, not a startup failure (see spec Assumptions addendum)
- [X] T034 Verify SC-001 through SC-005 are each covered by a passing test; produce a coverage map (below) and fix any gap — all five confirmed covered, no gaps
- [X] T035 [P] Update the solution README noting spec 018 is evaluator-only and that 009/012/016 integration is a separate, future refactor

**SC coverage map:**

| SC | Covered by |
|---|---|
| SC-001 | `SharingSingleImplementationTests` (T022) + `ISharingPolicyService` having no client-trusted input parameter (T020) |
| SC-002 | `SharingPolicyEvaluatorTests` (T016) |
| SC-003 | `SharingPolicyEvaluatorTests` (T023) |
| SC-004 | `SharingPolicyEvaluatorTests` (T024) |
| SC-005 | `SharingPolicyEvaluatorTests` (T025) |
| SC-006 | **Deferred** — requires a real persona/prompt/data-product test corpus that does not exist yet (009/012/016 are spec-only); not silently skipped |
| SC-007 | **Deferred** — requires a real persisted grant to revoke-or-not; 018 persists nothing, so this is only meaningfully testable once a consuming spec exists |

---

## Dependencies & Execution Order

- **Setup** → **Foundational** block everything.
- **US1 + US2** (both P1) together form the MVP — US1 alone is only vocabulary with no policy teeth; US2 is where FR-002's actual role-gating lands. Both depend only on Foundational.
- **US3** (P2) depends on Foundational and reuses US2's evaluator/service (T027/T028 extend the same `Evaluate` method US2 built) but is independently testable via its own scenarios.
- **US4** (P2) depends only on Foundational (`ShareTarget` from T003/T011) — independent of US2/US3.
- Polish last; T032 depends on all prior phases; T034 depends on T016/T023/T024/T025.

**Parallel opportunities**: T001/T002; T003/T004/T005/T007/T008 (Foundational, different files); all `[P]` test tasks within a story; US1 and US4 can proceed in parallel once Foundational lands (both only touch `ShareTarget`); US3 should follow US2 since it extends the same `Evaluate` method, but its tests can be drafted in parallel.

## Implementation Strategy

### MVP First (User Story 1 + User Story 2)

Unlike a typical single-story MVP, **US1 alone has no independently valuable behavior** — it only defines the `ShareTarget` vocabulary, since 018 has no resource to enforce access against. Ship **US1 + US2 together** as the MVP: the full baseline evaluator (admin bypass, per-role individual/group rules) with no overrides active yet.

1. Complete Phase 1 (Setup) + Phase 2 (Foundational).
2. Complete Phase 3 (US1) + Phase 4 (US2) together — **MVP checkpoint**: `ISharingPolicyService` correctly resolves every role × target-type combination with no override active.
3. Add Phase 5 (US3) — global overrides layer on top.
4. Add Phase 6 (US4) — access-level default/explicit split (independent of US2/US3, can be done anytime after Foundational).
5. Polish.

### Incremental Delivery

1. Setup + Foundational → domain vocabulary and config classes compile.
2. US1 + US2 → MVP: a fully correct, fully tested role-based sharing decision, ready for 009/012/016 to start consuming (in a future spec).
3. US3 → emergency/admin override control layered on top, no MVP rework needed.
4. US4 → access-level shape, independent, can slot in anytime.
5. Polish → quickstart validated, SC coverage confirmed, README updated.

## Notes

- [P] tasks = different files, no dependencies.
- [Story] label maps task to specific user story for traceability.
- No task in this list touches specs 009, 012, or 016 or any Web-layer endpoint — this feature is Domain/Application/Infrastructure only, per plan.md's Project Structure.
- Commit after each task or logical group; stop at either checkpoint to validate independently.
