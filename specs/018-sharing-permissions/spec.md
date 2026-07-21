# Feature Specification: Sharing & Permissions Policy

**Feature Branch**: `018-sharing-permissions`

**Created**: 2026-07-21

**Status**: Draft

**Input**: Derived from `docs/PRODUCT_REQUIREMENTS_DOCUMENT.md` §4.10 (Sharing & Permissions) and `docs/prd-decomposition-plan.md`'s call-out that sharing is "currently scattered across 009/012/016; PRD treats it as one cross-cutting policy (global overrides, per-role targets) — worth centralizing into one spec the others reference." This spec defines the canonical, resource-agnostic sharing policy — share targets (individuals/groups), the configurable per-role policy governing valid targets, global emergency/administrative overrides, and the read-vs-edit access-level distinction — that personas (spec 009), data products (spec 012), and prompts (spec 016) each currently reimplement in part. It does not cover any single resource type's CRUD, ownership-transfer, or visibility mechanics beyond the sharing rules those specs already reference; it also does not cover the refactor of 009/012/016 onto this policy (see Assumptions).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Share an owned resource with an individual or a group (Priority: P1)

An owner of a persona, prompt, or data product shares it with either a specific individual (by identity) or a predefined organizational group (e.g., admins, staff, faculty, students), and collaborators/viewers gain access accordingly.

**Why this priority**: This is the base capability every other rule in this spec constrains. Without a single, resource-agnostic definition of "what a share target is" (individual vs. group) and how it grants access, there is nothing for the role-based policy or global overrides to govern. It is the foundation the other three stories build on.

**Independent Test**: As a resource owner, share a resource with one individual and with one group target, and confirm both grantees gain the expected access (per Story 4's read/edit rule) while non-grantees do not.

**Acceptance Scenarios**:

1. **Given** an owner of a persona, prompt, or data product, **When** they add an individual as a share target, **Then** that individual gains read access to the resource.
2. **Given** an owner of a persona, prompt, or data product, **When** they add a predefined group (e.g., `faculty`, `students`, `admins`) as a share target, **Then** every member of that group gains read access.
3. **Given** a resource shared with neither a given user nor any group they belong to, **When** that user attempts to access it, **Then** access is denied.
4. **Given** a resource shared with an individual or group, **When** the owner removes that share target, **Then** the previously-granted access is revoked for future requests.

---

### User Story 2 - Configurable, role-based sharing policy constrains valid share targets (Priority: P1)

A caller of any role (admin, faculty, student, or other configured role) attempts to share a resource, and the system enforces — server-side, on every entry point — which target types (individual vs. group) and which specific groups that role is permitted to use.

**Why this priority**: Per constitution Principle II (server-side-only authorization), share-target validity cannot be a client-side or UI-only convention — every existing spec (009, 012, 016) already asserts some version of "sharing targets are role-gated," but each does so with its own hardcoded rule. This story is what makes Story 1's base capability safe to expose, so it is equally P1.

**Independent Test**: As an admin, a faculty caller, and a student caller, attempt every combination of {individual, each configured group} as a share target directly against the API (not the UI), and confirm each attempt's outcome matches the configured per-role policy.

**Acceptance Scenarios**:

1. **Given** an admin caller, **When** they share a resource with any individual or any predefined group, **Then** the share succeeds.
2. **Given** a faculty caller, **When** they share with an individual, **Then** the share always succeeds; **When** they share with a group, **Then** the share succeeds only if group sharing is enabled for faculty and the target group is one of the groups configured as faculty-eligible (e.g., faculty, students).
3. **Given** a student caller, **When** they share with an individual, **Then** the share always succeeds; **When** they share with a group, **Then** the share succeeds only if group sharing is enabled for students and the target group is one of the groups configured as student-eligible (e.g., students only).
4. **Given** any caller whose role/target combination is not permitted by the configured policy, **When** they submit the share request directly to the API (bypassing any UI restriction), **Then** the request is rejected server-side with no partial share recorded.
5. **Given** the same caller and target combination evaluated through the UI and through a direct API call, **When** both are compared, **Then** they produce identical accept/reject outcomes.

---

### User Story 3 - Global sharing overrides for emergency/administrative control (Priority: P2)

An administrator needs to immediately constrain or relax sharing platform-wide, independent of the resource-owning users' per-role entitlements — for example, disabling all group sharing during an incident, restricting sharing to admins only, or ensuring specific groups (e.g., an `announcements` group) are always shareable regardless of per-role policy.

**Why this priority**: This is administrative/emergency control layered on top of Stories 1-2's baseline mechanics — necessary for operational safety and incident response, but it does not block a first shippable version of sharing itself, so it ranks below the foundational stories.

**Independent Test**: With each override active in turn (disable-all-group-sharing, admin-only mode, globally-allowed-groups list), attempt representative share requests from multiple roles and confirm the override's effect is applied consistently and takes precedence over the per-role policy where the two conflict.

**Acceptance Scenarios**:

1. **Given** the disable-all-group-sharing override is active, **When** any non-admin caller (regardless of role or configured per-role group entitlement) attempts to share with any group, **Then** the request is rejected; individual sharing is unaffected.
2. **Given** admin-only sharing mode is active, **When** any non-admin caller attempts to share a resource with any target (individual or group), **Then** the request is rejected; admin-initiated shares continue to succeed.
3. **Given** a globally-allowed-groups list containing a group (e.g., `announcements`), **When** any caller whose per-role policy would otherwise disallow that group shares with it, **Then** the share succeeds (the global allow-list relaxes, rather than is overridden by, per-role restrictions) — unless disable-all-group-sharing or admin-only mode is simultaneously active, in which case those take precedence.
4. **Given** any global override is toggled off, **When** subsequent share requests are evaluated, **Then** the per-role policy from Story 2 governs again with no residual effect from the deactivated override.
5. **Given** shares that were already granted before an override was activated, **When** the override is active, **Then** previously-granted access is not automatically revoked — the override constrains new share operations, not existing grants (see Edge Cases).

---

### User Story 4 - Shared resources default to read access; edit requires explicit collaborator designation (Priority: P2)

A resource is shared with an individual or group, and the recipient's access level (read-only vs. read/write) depends on whether they were designated as a collaborator, not merely on being a share target.

**Why this priority**: This is the access-level contract every sharing consumer depends on to avoid over-granting write access — a real but already-partially-implemented rule (012 already separates `collaborators` edit rights from `sharedWith` read rights); ranked P2 because it refines Stories 1-2's grant mechanics rather than gating whether sharing can happen at all.

**Independent Test**: Share a resource with a plain (non-collaborator) share target and separately with a designated collaborator, and confirm the former can read but not edit, while the latter can both read and edit.

**Acceptance Scenarios**:

1. **Given** a resource shared with an individual or group without collaborator designation, **When** that recipient accesses it, **Then** they can read it but any edit attempt is rejected.
2. **Given** a resource shared with an individual explicitly designated as a collaborator, **When** that recipient accesses it, **Then** they can both read and edit it.
3. **Given** a collaborator (edit rights), **When** they attempt an owner-only action (e.g., delete, ownership transfer, changing the resource's own sharing policy), **Then** the action is rejected — collaborator status grants edit, not ownership-equivalent rights.
4. **Given** a group share without collaborator designation, **When** a member of that group accesses the resource, **Then** they receive read-only access identical to an individually-shared, non-collaborator recipient.

### Edge Cases

- When disable-all-group-sharing or admin-only mode is activated, do previously-granted group-based or individual shares stop granting access immediately, or only block new share operations? Per Story 3 Scenario 5, this spec treats overrides as forward-looking (new-share-blocking) only; revoking existing access is a separate, explicit action, not an implicit side effect of toggling an override.
- What happens when a role's configured group-target list is changed (e.g., faculty's allowed groups narrowed) while existing shares already target a now-disallowed group? Existing shares are not retroactively invalidated by this spec; only new share attempts are validated against the current policy.
- How does the policy resolve a caller who belongs to multiple roles (e.g., both faculty and admin, or a group-membership role plus an individual override)? The most permissive applicable role's policy governs, consistent with admin's "share with all groups and individuals" baseline.
- **Data products currently have no `@students` (or any student-facing) group-sharing branch at all (spec 012) — only `@employees`/`@contractors`/`@{organizationId}` tokens, with students reaching data products solely through a persona's configured `dataProducts` list.** This spec treats that as a real gap relative to the PRD, not a permanent design choice to preserve: PRD §4.10 explicitly names data products as governed by this same cross-cutting policy, with no data-product carve-out, and defines student group-sharing as a first-class, per-role-configurable capability. Reasoning: 012's exclusion predates a unified policy and was a reasonable stopgap absent one; once 012 is refactored to depend on this spec (out of scope here, see Assumptions), it should gain student-eligible group sharing subject to the same per-role configuration as personas/prompts, rather than remaining a hardcoded exception. Closing this gap in 012 itself is explicitly out of scope for this spec.
- The predefined group vocabulary differs across today's implementations — personas/prompts imply organizational roles (`admins`/`faculty`/`students`) while data products use `@employees`/`@contractors`/`@{organizationId}` tokens. This spec treats "group" as an abstract, deployment-configured target catalog rather than a fixed enum; reconciling the two vocabularies into one catalog is a consequence of the 009/012/016 refactor, not something this spec resolves unilaterally.
- What happens when the globally-allowed-groups list includes a group that a role's per-role policy also independently permits? No conflict — the two are additive (Story 3, Scenario 3); the global list only ever adds permitted targets, never removes them.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST support sharing an owned persona, prompt, or data product with individuals (by identity) and with predefined organizational groups, as the single shared mechanism all resource types use. *(REQ-SHARE-1)*
- **FR-002**: The system MUST enforce a configurable, role-based sharing policy, server-side, that determines for each role whether it may share with individuals, whether it may share with groups, and which specific groups it may target. *(REQ-SHARE-2)*
- **FR-003**: The admin role's policy MUST permit sharing with all individuals and all predefined groups.
- **FR-004**: The faculty role's policy MUST always permit sharing with individuals, and MUST permit sharing with groups only when group sharing is enabled for faculty, constrained to the configured faculty-eligible group set (e.g., faculty, students).
- **FR-005**: The student role's policy MUST always permit sharing with individuals, and MUST permit sharing with groups only when group sharing is enabled for students, constrained to the configured student-eligible group set (e.g., students only).
- **FR-006**: Role-based policy evaluation MUST produce identical accept/reject outcomes regardless of whether the share request originates from a UI surface or a direct API call.
- **FR-007**: The system MUST support a global override that disables all group sharing platform-wide, independent of any role's configured group entitlement, without affecting individual sharing.
- **FR-008**: The system MUST support a global "admin-only sharing" override that restricts all new sharing (individual and group) to admin callers only.
- **FR-009**: The system MUST support a global list of allowed group targets that are shareable by any role regardless of that role's own per-role group-target configuration (e.g., an `announcements` group).
- **FR-010**: When disable-all-group-sharing or admin-only mode is active, that override MUST take precedence over both the per-role policy and the globally-allowed-groups list for any conflicting share attempt. *(REQ-SHARE-3)*
- **FR-011**: Global overrides MUST govern new share operations only; they MUST NOT implicitly revoke access already granted before the override was activated.
- **FR-012**: A resource shared with an individual or group MUST grant read access by default. *(REQ-SHARE-4)*
- **FR-013**: Edit access to a shared resource MUST require the recipient be explicitly designated a collaborator; collaborator designation MUST NOT be implied merely by being a share target.
- **FR-014**: Collaborator (edit) status MUST NOT grant owner-only actions (e.g., delete, ownership transfer, modifying the resource's own share targets) — those remain owner/admin-gated per each resource type's own authorization rules.
- **FR-015**: All sharing-policy decisions (role-based validity and global-override effect) MUST be evaluated server-side on every entry point that creates or modifies a share, never trusted from client input.

### Key Entities *(include if feature involves data)*

- **ShareTarget**: an individual (by identity) or a predefined group; the unit a resource is shared with. Carries an access level (`read` or `collaborator`/edit).
- **Group**: a predefined organizational target (e.g., admins, staff, faculty, students, or a resource-specific/deployment-defined token such as `@employees`/`@announcements`); membership is resolved by existing role/identity data, not owned by this spec.
- **RoleSharingPolicy**: the per-role configuration governing whether a role may target individuals and/or groups, and which specific groups it may target (e.g., admin/faculty/student policies described in this spec).
- **GlobalSharingOverride**: platform-wide configuration comprising `disableAllGroupSharing` (bool), `adminOnlyMode` (bool), and `globallyAllowedGroups` (list) — evaluated ahead of, and able to override, per-role policy.
- **SharingDecision**: the server-side evaluation result (allow/deny) produced by combining a caller's role, the requested target, `RoleSharingPolicy`, and any active `GlobalSharingOverride` — the canonical decision every resource-type spec (009, 012, 016) should consume rather than reimplement.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of share requests across all roles are evaluated against the role-based policy server-side, with 0 requests accepted or rejected based on client-supplied validity signals alone.
- **SC-002**: Across a full role x target-type test matrix (admin/faculty/student x individual/each configured group), 100% of outcomes match the configured per-role policy, with identical results whether invoked via UI or direct API.
- **SC-003**: 100% of group-share attempts are rejected while disable-all-group-sharing is active, across all non-admin roles, with 0 impact on concurrent individual-share attempts.
- **SC-004**: 100% of non-admin share attempts (individual or group) are rejected while admin-only mode is active.
- **SC-005**: 100% of share attempts targeting a globally-allowed group succeed regardless of the caller's per-role group entitlement, unless disable-all-group-sharing or admin-only mode is simultaneously active.
- **SC-006**: 0 edit operations succeed for a share recipient who was granted read-only (non-collaborator) access, across a test corpus spanning personas, prompts, and data products.
- **SC-007**: 100% of previously-granted shares remain functional immediately after a global override is activated (no implicit revocation), verified across all three override types.

## Assumptions

- Specs 009 (persona sharing), 012 (data-product sharing/visibility), and 016 (prompt sharing) each currently implement their own sharing-target validation independently (009's admin-vs-non-admin binary gate with a "documented company-wide override," 012's owner+collaborators+sharedWith union with `@employees`/`@contractors`/`@{organizationId}` group tokens and no student branch, and 016's explicit reuse of 009's rules). Per constitution Principle IV (one canonical implementation per concern), all three SHOULD be refactored to depend on this spec's `SharingDecision` as their single source of truth for share-target validity and global overrides, rather than re-validating independently. **Implementing that refactor — modifying 009, 012, or 016 — is explicitly out of scope for this spec**, which defines the canonical policy those specs should adopt.
- 009's current model is coarser than the three-tier (admin/faculty/student) role policy this spec defines: it distinguishes only admin vs. non-admin, with no independent faculty/student group-target configurability. This spec's per-role policy (FR-003 through FR-005) supersedes 009's binary model as the target behavior; 009 itself is not modified here.
- The specific group catalogs (e.g., which named groups exist, which are faculty-eligible vs. student-eligible, and what belongs in the globally-allowed list) are treated as deployment/tenant-level configuration, not hardcoded by this spec. This spec defines the enforcement mechanism and default role shapes described in PRD §4.10, not the concrete production values of that configuration.
- The administrative surface for setting/toggling global overrides (e.g., an admin settings UI or config store) is out of scope for this spec; this spec defines the effect of an override being active, not how an administrator activates one.
- Data products' current absence of a student group-sharing branch (012) is treated as a gap this canonical policy should eventually close via 012's refactor (see Edge Cases for reasoning), not as a scope boundary this spec itself preserves.
- "Collaborator" as the edit-access designation is the same concept already named `collaborators` in 009/012/016; this spec does not rename it, only defines its access-level semantics (read-by-default, edit-by-explicit-designation) as the canonical rule.
