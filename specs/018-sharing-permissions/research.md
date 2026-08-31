# Phase 0 Research: Sharing & Permissions Policy

**Feature**: 018-sharing-permissions | **Date**: 2026-08-31

All items below resolve the Technical Context; the spec's single clarification (stateless evaluator, no persisted store) was already resolved during `/speckit.clarify` and is not re-litigated here.

---

## D1: No persisted store — pure `IOptions`-bound evaluator

**Decision**: `RoleSharingPolicy` and `GlobalSharingOverride` are read from static, deployment-level configuration via two `IOptions`-bound classes; there is no database, cache, or admin mutation API in this feature.

**Rationale**: Directly resolved by the spec's own Clarifications session (2026-08-28). Unlike spec 014 (which is SQL/Redis-backed despite also being "config-driven"), 018 has no equivalent of `SystemModelConfig`'s admin-mutable rows — the spec's Assumptions explicitly frame changing either config as an ops/config-deployment action, not an in-app one.

**Alternatives considered**: A SQL-backed `RoleSharingPolicy` table mirroring 014's `SystemModelConfig` (rejected — the clarification explicitly ruled this out to keep 018 evaluator-only); a feature-flag service (rejected — no such service exists in this codebase, and `IOptions` is the established pattern for exactly this shape, per `RoleDerivationMappingOptions`/`ContentSafetyOptions`).

---

## D2: Role dimension keys off the real `RoleName` enum, not the spec's illustrative names

**Decision**: `RoleSharingPolicyOptions` is keyed by the already-implemented `RoleName` enum (`Admin`, `Employee`, `Contractor`, `Student` — `src/EnterpriseAIPlatform.Application/Authorization/RoleName.cs`), not by the spec's illustrative "faculty"/"student" labels.

**Rationale**: Spec 002 (already implemented) derives exactly four independent role flags via `RoleFlags`/`UserModel.Roles` — there is no `Faculty` flag anywhere in the codebase. Spec 018's own Assumptions section already anticipates this: it says PRD §4.10's role shapes are "not hardcoded by this spec" and that the concrete group/role catalog is deployment configuration. Spec 012 (data products) independently reached the same mapping already — its `@employees`/`@contractors` group tokens are the same real-world "faculty-like" population the PRD calls "faculty." Keying `RoleSharingPolicyOptions` off `RoleName` means FR-004 ("faculty role's policy") is satisfied by configuring the `Employee` entry, and FR-005 ("student role's policy") by the `Student` entry — no spec-text mismatch, no invented role flag.

**Alternatives considered**: Adding a new `Faculty` flag to spec 002's `RoleFlags` (rejected — out of scope for 018, which must not modify spec 002; also spec 018's Assumptions already treat the PRD's named roles as illustrative, not literal). Keying policy by a free-form string role name instead of the `RoleName` enum (rejected — loses compile-time safety and the DataAnnotations validation pattern every other `Options` class in this codebase uses).

**Addendum (post-`/speckit.analyze`)**: `RoleName.Contractor` has no corresponding spec.md FR/SC at all — spec 018 only defines policy for the "faculty"/"student" roles it maps to `Employee`/`Student`. Requiring an explicit `Roles` entry for `Contractor` would force guessing an unspecified product decision. Resolved by making `Roles` entries optional per role: a `RoleName` absent from config evaluates as the most restrictive `RolePolicy.Default` (`GroupSharingEnabled: false, AllowedGroups: []`) rather than failing startup or guessing a permissive value — safe until a `/speckit.clarify` pass defines Contractor's actual policy. `Admin` remains explicitly rejected as a key regardless (D3).

---

## D3: Admin bypass and individual-sharing are hardcoded evaluator rules, not configurable entries

**Decision**: The evaluator special-cases two things outside of `RoleSharingPolicyOptions`: (1) `RoleName.Admin` always allows both individual and group targets (FR-003), and (2) individual-target sharing is always allowed for every non-admin role (FR-004/FR-005's "always permit sharing with individuals") — subject only to `AdminOnlyMode`. Only *group*-target eligibility per non-admin role is genuinely config-driven (`GroupSharingEnabled`, `AllowedGroups`).

**Rationale**: Both rules are worded as spec-level `MUST`s with no "unless configured otherwise" clause (FR-003/004/005), unlike group eligibility which the spec explicitly calls "configurable." Making them config-driven would let a misconfiguration silently violate a hard requirement — inconsistent with Principle III (fail loud) applied to a security decision: a required invariant should not depend on config being set correctly.

**Alternatives considered**: Fully config-driven policy (an `AllowIndividual` bool per role, wildcard `AllowedGroups: ["*"]` for admin) — rejected because it makes FR-003/004/005 satisfiable by config alone, meaning a config error could silently deny/allow in violation of a `MUST`; hardcoding the invariant-level rules and configuring only the genuinely-variable part (which groups) is safer and matches how 014 hardcodes its `EffectiveModelAccess` boolean formula rather than making the intersection itself configurable.

---

## D4: Multi-role resolution — most-permissive-role-wins

**Decision**: `SharingPolicyEvaluator.Evaluate` accepts the caller's full `RoleFlags` (all roles simultaneously, matching spec 002's "independent, not mutually exclusive" role model) and returns `Allow` if *any* applicable role's policy would allow the specific request, after global overrides are applied.

**Rationale**: Directly specified in the spec's Edge Cases: "The most permissive applicable role's policy governs, consistent with admin's... baseline." Since `RoleFlags` already models multiple simultaneous flags (e.g., a user could be both `Employee` and `Admin`), the evaluator must iterate all set flags, not assume a single role.

**Alternatives considered**: Picking a single "highest" role via a fixed precedence order (rejected — the spec's rule is explicitly "most permissive for *this* request," which can differ from a fixed role-precedence ordering, e.g., a hypothetical role with broader group access but no admin flag).

---

## D5: Global-override precedence and additive allow-list — encoded as an ordered check sequence

**Decision**: The evaluator applies checks in this fixed order for a group-target request: (1) `AdminOnlyMode` active and caller not admin → deny; (2) caller is `Admin` → allow; (3) `DisableAllGroupSharing` active and caller not admin → deny; (4) target group ∈ `GloballyAllowedGroups` → allow; (5) any of caller's roles has `GroupSharingEnabled` and target group ∈ that role's `AllowedGroups` → allow; (6) otherwise deny. Individual-target requests skip straight to the `AdminOnlyMode` check only.

**Rationale**: This ordering is a direct transcription of FR-007 through FR-010 and Story 3's five acceptance scenarios — in particular Scenario 3's explicit precedence note ("unless disable-all-group-sharing or admin-only mode is simultaneously active, in which case those take precedence") and FR-010's blanket precedence rule. Encoding it as an ordered sequence (rather than a boolean formula, unlike 014's single-expression `EffectiveModelAccess`) keeps each `SharingDecision.Reason` traceable to one FR for testability (Principle VI).

**Alternatives considered**: A single boolean expression (`allow = (!adminOnly || isAdmin) && (isAdmin || !disableGroups || target.Type==Individual) && (...)`) — rejected as harder to unit-test per-scenario and harder to produce a debuggable `Reason` code from, compared to 014's `EffectiveModelAccess` where a single formula was sufficient because there was no multi-override precedence to disambiguate.

---

## D6: Evaluator shape — static pure function + thin DI wrapper, mirroring `ModelAccessEvaluator`

**Decision**: `SharingPolicyEvaluator.Evaluate(RoleFlags callerRoles, ShareTargetRequest request, RoleSharingPolicyOptions rolePolicy, GlobalSharingOverrideOptions globalOverride) → SharingDecision` is a `static` method in `EnterpriseAIPlatform.Application.Sharing`, taking plain values (no DI, no async, fully deterministic) — identical in spirit to `ModelAccessEvaluator.ComputeEffectiveAccess`. A thin `SharingPolicyService : ISharingPolicyService` in Infrastructure resolves `IOptionsSnapshot<RoleSharingPolicyOptions>`/`IOptionsSnapshot<GlobalSharingOverrideOptions>` and the caller's `UserModel`, then delegates to the static function.

**Rationale**: This is the established precedent in this exact codebase for "config-driven, stateless decision" services (014's `ModelAccessEvaluator`), confirmed by direct inspection of `src/EnterpriseAIPlatform.Application/ModelAccess/ModelAccessEvaluator.cs`. Keeping the decision logic in a static, DI-free function makes the full combinatorial test matrix (SC-002 through SC-005) trivial to unit test without mocking anything.

**Alternatives considered**: Putting the logic directly in `SharingPolicyService` (rejected — loses the DI-free unit-testability that 014's split already demonstrates is valuable, and every future consumer (009/012/016) will want to call the pure function directly in their own unit tests too).

---

## D7: Config validation — DataAnnotations + `ValidateOnStart`, no FluentValidation

**Decision**: `RoleSharingPolicyOptions` and `GlobalSharingOverrideOptions` use `System.ComponentModel.DataAnnotations` attributes and are registered with `.ValidateDataAnnotations().ValidateOnStart()`.

**Rationale**: This is the only validation convention used anywhere in the codebase today (`RoleDerivationMappingOptions`); there is no FluentValidation dependency anywhere in `src/`. Failing at startup on malformed config is a direct application of Principle III (fail loud) to a config surface that is otherwise untestable by an integration test (since there's no UI/API to exercise the bad-config path against).

**Alternatives considered**: Runtime validation on first use (rejected — a malformed sharing policy is a security-relevant misconfiguration; failing at startup, before any request is served, is strictly safer than discovering it on the first share attempt).

---

## D8: No HTTP routes, no `route-table.md` — internal service contract only

**Decision**: 018 does not add any Web-layer endpoint. Its only contract is the internal `ISharingPolicyService` interface (see `contracts/service-interfaces.md`) and the `appsettings.json` shape both options classes bind to (see `contracts/config-schema.md`).

**Rationale**: The spec's Assumptions explicitly place "the administrative surface for setting/toggling global overrides" out of scope — there is no admin UI/API to route to, and 009/012/016 (018's future callers) are not yet implemented in `src/`, so there is no existing HTTP entry point to wire this into yet either. A `route-table.md` documenting zero routes would add no information.

**Alternatives considered**: Producing an empty/placeholder `route-table.md` for template-consistency with 014 (rejected — `contracts/README.md` states the absence explicitly instead, which is more informative than an empty file).

---

**All Technical Context items resolved; no `NEEDS CLARIFICATION` markers remain.**
