# Feature Specification: Persona CRUD & Authorization

**Feature Branch**: `009-persona-crud-authorization`

**Created**: 2026-07-20

**Status**: Draft

**Input**: Derived from SSD_Document.md §3.5 (Persona Management & Persona Studio) — reframed from "as-is" discovery findings into target requirements, scoped to persona CRUD, authorization, sharing, and ownership-transfer only (the AI-assisted builder and live-preview surfaces are covered by a separate spec). Cross-checked against `docs/PRODUCT_REQUIREMENTS_DOCUMENT.md` §4.7 (Personas, REQ-PERSONA-1 through REQ-PERSONA-8) per `docs/prd-decomposition-plan.md`, which routes §4.7 to this spec and 010 noting: "Good coverage already; add REQ-PERSONA-7 (A2A publish) cross-link to spec 011." Source facts: `EnsurePersonaOperation` already correctly grants admins/owner/hashed-collaborators full access and any student read-only access to lesson personas, otherwise returning a deliberately non-revealing `UNAUTHORIZED`; non-admins are already blocked from deleting/writing a lesson persona even when the read gate passes; a non-admin's submitted `isLessonPersona` is already discarded on update; non-admin listings already exclude lesson personas; sharing targets are already role-gated. Against that mostly-correct baseline, three gaps were found: ownership transfer is implemented as delete-then-recreate under the new owner's Cosmos partition key with no transaction, so a failure after the delete permanently loses the persona; the A2A credential `apiKey` is stripped only by the "public" persona accessor while the raw accessor remains directly importable by client code, making the protection opt-in per call site rather than structural; and the "`dataProducts` required when the `DataProduct` extension is selected" rule is enforced in three UI call sites but not in the shared Zod schema, so a direct API call can bypass it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ownership transfer never loses a persona (Priority: P1)

An admin transfers ownership of a persona from one user to another, and the persona must exist, fully and correctly, under exactly one owner at every point during and after the transfer — including if the transfer fails partway through.

**Why this priority**: Ownership transfer is currently implemented as delete-then-recreate under the new owner's Cosmos partition key with no transactional guarantee. A failure after the delete step (network blip, validation error on recreate, Cosmos throttling) permanently destroys the persona with no recovery path. This is an unrecoverable data-loss bug and outranks every other item in this spec.

**Independent Test**: Trigger an ownership transfer and force a failure immediately after the delete step (e.g., inject a failure in the recreate call); confirm the persona still exists — either restored to the original owner or fully present under the new owner — with no state in which it exists under neither.

**Acceptance Scenarios**:

1. **Given** a persona owned by User A, **When** an admin transfers ownership to User B and the transfer completes normally, **Then** the persona exists exactly once, under User B, with all fields intact.
2. **Given** a persona owned by User A, **When** an admin transfers ownership to User B and the recreate step fails after the delete step has already run, **Then** the persona is either restored under User A or successfully finalized under User B — it is never permanently lost.
3. **Given** a failed transfer that was recovered per Scenario 2, **When** the admin or owner retries the transfer, **Then** the retry succeeds without manual data-recovery steps.

---

### User Story 2 - Access, edit, and delete rights are consistently gated (Priority: P1)

Any caller — admin, owner, collaborator, student, or unauthenticated/unrelated user — interacts with a persona (read, list, edit, delete, or reassign the `isLessonPersona` flag), and the system must apply the same ownership/role/lesson rules regardless of entry point (UI or direct API call).

**Why this priority**: This is the foundational authorization surface every other persona operation depends on. Most of it is already implemented correctly (the central `EnsurePersonaOperation` gate, the lesson-persona write/delete block, the privilege-escalation guard on `isLessonPersona`, and lesson-persona exclusion from non-admin listings); this story locks that correct behavior in as an explicit, testable contract so it survives refactors, and closes the one related gap — sharing-target role-gating must hold even when exercised directly against the API rather than through the sharing UI.

**Independent Test**: As each of admin, owner, collaborator, student, and an unrelated authenticated user, attempt to read, list, edit, delete, and share a mix of regular and lesson personas directly via API, and confirm each outcome matches the expected role/ownership rule.

**Acceptance Scenarios**:

1. **Given** a regular (non-lesson) persona, **When** a user who is not the owner, not a hashed collaborator, and not an admin attempts to read, edit, or delete it, **Then** the system returns a non-revealing `UNAUTHORIZED` response indistinguishable from "not found" (no enumeration signal).
2. **Given** a lesson persona (`isLessonPersona === true`), **When** any authenticated student requests read access, **Then** access is granted read-only.
3. **Given** a lesson persona, **When** a non-admin (including its nominal collaborator) attempts to delete or write to it, **Then** the operation is blocked even though the read-access gate would otherwise permit access.
4. **Given** a non-admin caller submitting a persona update, **When** the payload includes a changed `isLessonPersona` value, **Then** the system discards that value and preserves the existing flag, regardless of payload content.
5. **Given** a non-admin user's persona listing request, **When** the response is returned, **Then** it contains no lesson personas (they remain reachable only via direct Canvas/LTI deep link).
6. **Given** a non-admin caller, **When** they attempt to share a persona with a group token (e.g., `@employees`) rather than an individual, **Then** the request is rejected unless a documented company-wide override applies — enforced identically whether invoked through the sharing UI or a direct API call.

---

### User Story 3 - The A2A credential never reaches client code (Priority: P2)

A developer adds a new UI surface (component, page, or API route) that fetches persona data for client-side rendering, without knowing about the existing "public" vs. "raw" accessor distinction.

**Why this priority**: Today, `apiKey` (the A2A credential) is stripped only by the "public" persona accessor (`PersonaPublicDTO`); the raw, non-stripped accessor is still directly importable by client components, and at least one already does. The protection is opt-in per call site rather than structurally enforced, so every new call site is a potential credential leak. This is a real security gap, but it is scoped to client-exposed data shape rather than data loss or broken authorization, so it ranks below Stories 1–2.

**Independent Test**: Search all code paths that place persona data into a client-rendered response or client-importable module, and confirm none can include a non-empty `apiKey` field, including newly written call sites that don't explicitly call a stripping function.

**Acceptance Scenarios**:

1. **Given** any code path that serializes persona data for client consumption, **When** the response is inspected, **Then** it never contains the `apiKey` field, regardless of which accessor function was called.
2. **Given** a new client component that imports a persona-fetching function without special knowledge of DTO stripping, **When** it renders persona data, **Then** `apiKey` is absent by construction (the type/shape returned to client code structurally excludes it), not merely absent by convention.
3. **Given** a server-side A2A code path that legitimately needs `apiKey` (e.g., `/api/agents/[id]` credential comparison), **When** it fetches persona data, **Then** it can still obtain `apiKey` via a distinct, clearly server-only accessor.

---

### User Story 4 - `dataProducts` requirement is enforced for every entry point (Priority: P3)

A caller creates or updates a persona with the `DataProduct` extension selected but no `dataProducts` entries, either through the UI or via a direct API call.

**Why this priority**: This rule is currently enforced in three separate UI call sites but not in the shared Zod schema, so a direct API call bypasses it, producing a persona that references the `DataProduct` extension with nothing to search. It's a real validation gap but lower severity than Stories 1–3 since it requires bypassing the normal UI and doesn't cause data loss or an authorization bypass.

**Independent Test**: Submit a persona create/update payload directly to the API with the `DataProduct` extension selected and an empty/absent `dataProducts` array, and confirm the request is rejected before persistence, independent of any UI-side validation.

**Acceptance Scenarios**:

1. **Given** a direct API payload with `extensions` including `DataProduct` and `dataProducts` empty or absent, **When** submitted to create or update a persona, **Then** the request is rejected with a field-specific validation error.
2. **Given** a direct API payload with `extensions` including `DataProduct` and at least one `dataProducts` entry, **When** submitted, **Then** the request succeeds (no false positives).
3. **Given** a payload without the `DataProduct` extension selected, **When** submitted with an empty `dataProducts` array, **Then** the request succeeds (the rule only applies when the extension is selected).

### Edge Cases

- What happens if an ownership-transfer retry is attempted while the persona is in the recovered/pending state from a prior failed attempt (Story 1) — must not create duplicate personas under both owners.
- How does the system behave when the same persona ID is targeted by a transfer and a concurrent edit/delete from another caller?
- Does the non-revealing `UNAUTHORIZED` response (Story 2, Scenario 1) remain identical in shape/timing for "does not exist" vs. "exists but forbidden," so response-time or payload differences can't be used for enumeration?
- What happens when a lesson persona's designated collaborator (not an admin) attempts to edit it — confirm the lesson-persona write block (Story 2, Scenario 3) applies even to collaborators, not just arbitrary non-admins.
- How does Story 3's structural guarantee interact with server-side rendering paths that both need `apiKey` (A2A invocation) and produce HTML potentially inspectable by the client — the guarantee must cover serialized/embedded data, not just JSON API responses.
- What happens when a persona is created via direct API with `DataProduct` selected and `dataProducts` supplied only as an empty-string array (not `undefined`/omitted) — must still trip the Story 4 validation.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Ownership transfer MUST be atomic or recoverable: a failure at any point during the transfer MUST leave the persona either intact under the original owner or fully present under the new owner, never lost or duplicated.
- **FR-002**: A failed ownership transfer MUST be safely retryable without manual data-recovery steps.
- **FR-003**: The central access gate MUST grant full access to admins, the persona's owner, and its hashed collaborators, and MUST grant read-only access to any student when `isLessonPersona === true`.
- **FR-004**: All other callers MUST receive a non-revealing `UNAUTHORIZED` response, indistinguishable from "not found," for both read and write attempts (enumeration prevention).
- **FR-005**: Non-admin callers MUST be blocked from deleting or writing to a lesson persona even when the read-access gate would otherwise permit access.
- **FR-006**: On update, a non-admin caller's submitted `isLessonPersona` value MUST be discarded server-side; the existing flag MUST be preserved regardless of payload content.
- **FR-007**: Persona listings returned to non-admin users MUST exclude lesson personas.
- **FR-008**: Sharing targets MUST be role-gated identically across all entry points: admins may share with group tokens; non-admins may share only with individuals, subject to documented company-wide overrides.
- **FR-009**: `apiKey` MUST be structurally excluded from any data shape returned to client-side code, regardless of which accessor function or call site is used — the exclusion must not depend on each call site remembering to strip it.
- **FR-010**: A distinct, clearly server-only accessor MUST remain available for legitimate server-side consumers (e.g., A2A credential comparison) that need `apiKey`.
- **FR-011**: The rule "`dataProducts` must contain at least one entry when the `DataProduct` extension is selected" MUST be enforced in the shared Zod schema used by every create/update entry point, not only in UI call sites.
- **FR-012**: Rejected persona operations (unauthorized access/edit/delete, blocked lesson-persona mutation, failed `dataProducts` validation) MUST leave existing persona state unchanged (no partial writes).

### Key Entities *(include if feature involves data)*

- **PersonaModel**: owned persona configuration — `id`, `userId` (hashed owner, also the Cosmos partition key), `model`, `name`/`description`/`personaMessage`, `extensions?`, `dataProducts?` (required non-empty when `DataProduct` extension selected), `sharedWith?`/`collaborators?`, `apiKey?` (A2A credential, server-only), `isLessonPersona?` (admin-managed, drives student read-only access and non-admin listing exclusion).
- **PersonaPublicDTO**: the client-safe projection of `PersonaModel` used everywhere persona data reaches client code; must structurally omit `apiKey`.
- **Ownership Transfer**: the operation moving a persona's partition key (`userId`) from one owner to another; must be atomic or recoverable rather than an unguarded delete-then-recreate.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 0 permanently-lost personas across a test corpus of ownership transfers with injected mid-transfer failures at every step boundary.
- **SC-002**: 100% of unauthorized read/edit/delete attempts (non-owner, non-collaborator, non-admin, non-eligible-student) receive the non-revealing `UNAUTHORIZED` response, with 0 distinguishable signal between "not found" and "forbidden."
- **SC-003**: 100% of non-admin lesson-persona delete/write attempts are blocked, including from designated collaborators.
- **SC-004**: 0 non-admin `isLessonPersona` payload values are ever persisted; the existing flag is preserved in 100% of non-admin update attempts.
- **SC-005**: 0 lesson personas appear in non-admin listing responses across the test corpus.
- **SC-006**: 0 client-reachable code paths expose a non-empty `apiKey` field, verified by a structural/type-level check plus a runtime scan of client-bound responses.
- **SC-007**: 100% of direct-API persona create/update calls with `DataProduct` selected and empty/absent `dataProducts` are rejected, with 0 false-positive rejections when `dataProducts` is populated or the extension isn't selected.

## Assumptions

- The AI-assisted persona builder (`/api/persona-builder`) and live draft preview (`/api/persona-preview`) are explicitly out of scope for this spec and covered separately.
- "Hashed collaborators" and the sharing-permission model (`getSharingPermissions`) are reused as-is from the existing cross-cutting sharing engine; this spec does not redesign that engine, only locks in its persona-specific application.
- The `IDatabaseProvider`'s batch/transaction capability (capped at 100 ops per partition) is assumed sufficient to implement FR-001's atomicity/recoverability requirement for a single persona's transfer; a distributed-transaction framework is not required.
- Lesson personas remain reachable only via direct Canvas/LTI deep link, not the general catalog; this spec does not change that discovery model, only the listing/authorization rules around it.
- **PRD §4.7 cross-check — REQ-PERSONA-1/3/5/8**: basic create/edit/delete/duplicate mechanics (REQ-PERSONA-1) capturing model, instructions, starting message, enabled tools, and attached knowledge collections are pre-existing, correctly-functioning baseline behavior this spec does not re-specify; its FRs address only the authorization/integrity gaps layered on top (ownership transfer, apiKey exclusion, `dataProducts` validation). Favoriting (REQ-PERSONA-3) is a per-user preference gated by the same read-access rule already covered by FR-003/FR-004 and is not separately re-specified. Lesson-persona read/edit/delete restrictions (REQ-PERSONA-5) are fully covered by FR-005 through FR-007 and User Story 2. Exclusion of sensitive fields like `apiKey` from client-facing responses (REQ-PERSONA-8) is covered by FR-009/FR-010 and User Story 3.
- **PRD §4.7 cross-check — REQ-PERSONA-4 (sharing/collaborators)**: FR-008's admin-vs-non-admin sharing-target gate is this spec's persona-specific enforcement point only. The canonical, resource-agnostic sharing policy (role-based target validity, global overrides, read-vs-collaborator access levels) is defined in `specs/018-sharing-permissions/spec.md`, whose three-tier admin/faculty/student policy explicitly supersedes 009's binary admin/non-admin model (see 018's Assumptions). This spec does not re-specify that policy and should eventually consume 018's `SharingDecision` as its source of truth (out of scope here).
- **PRD §4.7 cross-check — REQ-PERSONA-7 (A2A publish)**: publishing a persona as an A2A-callable agent secured by its `apiKey` is specified in `specs/011-a2a-agent-invocation-contract/spec.md` (AgentCard generation, `/api/agents/{personaId}` invocation, constant-time `apiKey` comparison, `a2aEnabled` gating). This spec's role is limited to owning the `apiKey`/`a2aEnabled` fields on `PersonaModel` and structurally excluding `apiKey` from client-facing DTOs (FR-009/FR-010); it does not re-specify A2A invocation mechanics.
- **PRD §4.7 cross-check — REQ-PERSONA-2 (start chat from a persona)**: starting a chat directly from a persona and applying its model/instructions/tools/data products is a chat-invocation/usage-flow concern, not a CRUD/authorization concern, so it is out of scope for this spec. It is also out of scope for spec 010 (AI-assisted builder/live-preview only). No existing spec in this repository currently covers it; it would belong with a chat/conversation-initiation spec, which does not yet exist. This is flagged here as an uncovered PRD requirement for a future decomposition-plan follow-up, not something this spec addresses.
