# Feature Specification: Prompt CRUD, Sharing & Ownership Transfer

**Feature Branch**: `016-prompt-crud-sharing-ownership-transfer`

**Created**: 2026-07-20

**Status**: Draft

**Input**: Derived from SSD_Document.md §3.9 (Domain: Prompt Library) — reframed from "as-is" discovery findings into target requirements. Source facts: `TransferPromptOwnerShip` trusts client-supplied JSON for `name`/`description`/`createdAt`/`sharedWith` instead of re-deriving those fields from the server-verified record (only ownership is actually checked); ownership transfer is implemented as delete-then-recreate under the new owner's partition key, with no rollback if the recreate fails; and total prompt-generator failure (primary + fallback model both fail) returns a plain-text 500 body inconsistent with the JSON content-type of the success path. Extended per `docs/PRODUCT_REQUIREMENTS_DOCUMENT.md` §4.8 (Prompts) to cover the full forward-looking requirement set — REQ-PROMPT-1 (create/edit/delete), REQ-PROMPT-2 (favoriting, sharing, ownership transfer), and REQ-PROMPT-3 (launch a chat pre-seeded from a prompt) — per `docs/prd-decomposition-plan.md`'s routing of §4.8 to this spec ("Good coverage already," with sharing itself deferred to spec 018's canonical policy).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ownership transfer cannot be used to inject arbitrary field values (Priority: P1)

An owner or admin transfers a prompt to a new owner. The transfer request is a direct API call carrying a JSON body.

**Why this priority**: Today `TransferPromptOwnerShip` only checks that the caller is the owner or an admin — it does not re-validate the field *values* in the request body. The recreated record's `name`, `description`, `createdAt`, and `sharedWith` are taken verbatim from client-supplied JSON, so a caller with valid transfer authorization can smuggle in arbitrary field values (e.g., a forged `createdAt`, an expanded `sharedWith` list, or altered `description` content) that were never legitimately set on the prompt. This is a data-integrity/privilege issue independent of whether the transfer mechanics themselves are safe, and it is exploitable by any caller who already has legitimate transfer rights — so it is the most urgent item in this spec.

**Independent Test**: As an authorized owner/admin, issue a transfer request whose JSON body sets `name`, `description`, `createdAt`, and `sharedWith` to values that differ from the prompt's current server-side record, and confirm the resulting prompt (under the new owner) retains the original server-side values for all fields except owner, unaffected by the client-supplied values.

**Acceptance Scenarios**:

1. **Given** a prompt with server-side `name="A"`, `description="B"`, `createdAt=T1`, `sharedWith=["x@co.com"]`, **When** the owner submits a transfer request with a body claiming `name="HACKED"`, `description="HACKED"`, `createdAt=T2`, `sharedWith=["attacker@evil.com"]`, **Then** the transferred prompt has `name="A"`, `description="B"`, `createdAt=T1`, `sharedWith=["x@co.com"]` — every client-supplied field value is ignored.
2. **Given** a transfer request from an admin acting on another user's prompt, **When** the request body includes any field beyond the new owner identifier, **Then** those extra fields have no effect on the resulting record.
3. **Given** a transfer request from a caller who is neither the prompt's owner nor an admin, **When** the request is submitted, **Then** it is rejected and no field values (client-supplied or server-derived) are written.

---

### User Story 2 - Ownership transfer is atomic and recoverable on failure (Priority: P1)

An owner or admin transfers a prompt to a new owner, and the underlying write to the new owner's partition fails partway through.

**Why this priority**: Ownership transfer is implemented as delete-then-recreate under the new owner's partition key (Cosmos partition key = `userId`), with no rollback if the recreate step fails after the delete has already succeeded — this permanently loses the prompt. Combined with Story 1, this makes the current transfer path both insecure and unsafe; both must be fixed together before ownership transfer can be considered production-ready, so this is equally P1.

**Independent Test**: Trigger a transfer where the recreate step is made to fail (e.g., simulated write failure) and confirm the original prompt still exists, fully intact, under the original owner — the operation reports failure rather than silently losing the record.

**Acceptance Scenarios**:

1. **Given** a prompt owned by User A, **When** a transfer to User B is initiated and the write under User B's partition fails, **Then** the prompt still exists, unchanged, under User A, and the transfer reports a failure result.
2. **Given** a prompt owned by User A, **When** a transfer to User B completes successfully, **Then** the prompt exists exactly once, under User B, and no longer under User A.
3. **Given** a transfer failure has been reported, **When** the caller retries the transfer, **Then** the retry operates on the intact original record and can succeed without manual data-repair steps.

---

### User Story 3 - Consistent response format for prompt-generation failures (Priority: P2)

A user invokes AI-assisted prompt generation (`/api/promptGenerator`) and both the primary and fallback models fail.

**Why this priority**: Total failure currently returns a plain-text 500 body while every other path (success, and presumably partial failure) returns JSON — this breaks client-side error handling that expects a consistent content type, but it is a lower-severity correctness gap than the ownership-transfer issues above since it only affects error-message rendering, not data integrity or authorization.

**Independent Test**: Force both the primary and fallback models to fail and confirm the response is JSON with the same content-type as the success path, containing a structured, uniform error payload.

**Acceptance Scenarios**:

1. **Given** the primary model fails, **When** the fallback model also fails, **Then** the response has `Content-Type: application/json` and a structured error body consistent with the app-wide error envelope.
2. **Given** the primary model succeeds, **When** the response is returned, **Then** its content type matches the content type used for the total-failure case (both JSON).

---

### User Story 4 - Create, edit, and delete reusable prompt templates (Priority: P1)

A user creates a new prompt template (title + body/description), edits one they own or collaborate on, or deletes one they own.

**Why this priority**: REQ-PROMPT-1 (PRD §4.8). Basic CRUD is the foundation every other story in this spec (sharing, transfer, favoriting, launch-from-prompt) depends on; this spec previously only addressed authorization/response-format bugs on top of an assumed-existing CRUD implementation, so the CRUD contract itself is added here to close that gap.

**Independent Test**: As a user, create a prompt with a name and description, confirm it appears in the prompt library, edit its description and confirm the change persists, then delete it and confirm it no longer appears.

**Acceptance Scenarios**:

1. **Given** an authenticated user, **When** they submit a create request with non-empty `name` and `description`, **Then** a new prompt is persisted with that user as owner and appears in their prompt library.
2. **Given** a prompt the caller owns, administers, or collaborates on (per FR-001), **When** they submit an edit to `name`/`description`, **Then** the update is persisted and returned on the next read.
3. **Given** a prompt the caller owns, or an admin acting on any prompt, **When** they submit a delete request, **Then** the prompt is permanently removed and no longer returned by list/read/favorites operations for any user.
4. **Given** a caller who is neither owner, collaborator, nor admin, **When** they attempt to edit or delete the prompt, **Then** the request is rejected per FR-001.

---

### User Story 5 - Favorite prompts for quick access (Priority: P2)

A user marks prompts they own or can read as favorites, and can unfavorite them, independent of any other user's favorites.

**Why this priority**: REQ-PROMPT-2 (PRD §4.8) names favoriting alongside sharing and ownership transfer; the latter two are already covered by Stories 1-2, but favoriting has no requirement in this spec today. Ranked P2 as a convenience/organization layer on top of the P1 CRUD and access-control mechanics, mirroring persona favoriting's own priority.

**Independent Test**: As a user with access to several prompts, favorite two, confirm both are flagged as favorites on subsequent reads, unfavorite one, and confirm only the other remains.

**Acceptance Scenarios**:

1. **Given** a prompt the user owns or has read access to, **When** they favorite it, **Then** it is recorded in that user's own favorites list without altering the prompt's own record (owner, `sharedWith`, `collaborators`).
2. **Given** a favorited prompt, **When** the user unfavorites it, **Then** it no longer appears in their favorites list.
3. **Given** two different users who both have access to the same prompt, **When** one favorites it, **Then** the other user's favorites list is unaffected.
4. **Given** a prompt is deleted (Story 4), **When** any user's favorites list is subsequently read, **Then** the deleted prompt no longer appears there.

---

### User Story 6 - Launch a chat pre-seeded from a saved prompt (Priority: P2)

A user selects a saved prompt from the library (or via any other entry point that references a prompt), and a chat is populated with that prompt's content ready to use.

**Why this priority**: REQ-PROMPT-3 (PRD §4.8). The underlying mechanic — copying a prompt's `description` verbatim into the chat input, with no variable/placeholder substitution (FR-012) — is already specified, but no user story exercises it as a primary, named capability; this story closes that gap. Ranked P2 as the core "use a prompt" journey, depending on Story 4's CRUD existing first.

**Independent Test**: As a user, select a saved prompt from the library and confirm the chat input is populated with the prompt's `description` verbatim, matching the server-side record.

**Acceptance Scenarios**:

1. **Given** a saved prompt with `description` = "D", **When** the user selects it to start a chat, **Then** the chat input is populated with "D", unmodified.
2. **Given** a prompt whose `description` contains `[bracket]`-style placeholder text, **When** it is selected, **Then** the bracket text is copied verbatim with no substitution performed (per FR-012).

### Edge Cases

- What happens when a transfer request targets a new owner who does not exist or is not a valid recipient (e.g., malformed email/user identifier)?
- How does the system handle a transfer request submitted twice in quick succession (double-submit) against the same prompt?
- What happens when the primary model fails but the fallback model succeeds — does the response format match the JSON success/failure contract established here?
- How does `EnsurePromptOperation` behave when the prompt referenced in a transfer request was deleted or already transferred by another caller between page load and submission?
- What happens to a favorited prompt when it is transferred to a new owner (Story 2) — does it remain favorited for users other than the new/former owner?
- What happens when a user attempts to favorite a prompt they have no read access to?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `EnsurePromptOperation` MUST grant write access only to admins, the prompt's owner, or its collaborators.
- **FR-002**: `EnsurePromptOperation` MUST grant read access additionally to anyone the prompt is `sharedWith` (individual email or group token).
- **FR-003**: The system MUST require non-empty `name` and non-empty `description` for prompt create/update operations.
- **FR-004**: Prompt sharing targets MUST be role-gated per the canonical sharing policy defined in spec 018 (Sharing & Permissions Policy) — `RoleSharingPolicy` and any active `GlobalSharingOverride` — rather than a prompt-specific reimplementation of those rules. *(REQ-PROMPT-2, sharing half — see spec 018 for the policy itself)*
- **FR-005**: The ownership-transfer operation MUST re-derive `name`, `description`, `createdAt`, `sharedWith`, and all other non-ownership fields from the server-side record at the time of transfer, and MUST discard any client-supplied values for those fields.
- **FR-006**: The ownership-transfer operation MUST authorize the caller (owner or admin) before performing any write, consistent with existing behavior.
- **FR-007**: The ownership-transfer operation MUST be atomic or recoverable: if the write under the new owner's partition fails, the original record under the original owner MUST remain intact and the operation MUST report failure rather than leaving the prompt in a lost or duplicated state.
- **FR-008**: On successful transfer, the prompt MUST exist exactly once, under the new owner, and no longer under the original owner.
- **FR-009**: `EnsurePromptOperation` failures MUST continue to return a uniform, ambiguous `UNAUTHORIZED` result regardless of whether the true cause is "not found" or "forbidden," consistent with the equivalent persona-access pattern.
- **FR-010**: AI-assisted prompt generation MUST wrap user input in the existing fixed prompt-engineering meta-prompt and call a primary model, falling back once to a secondary model on primary failure.
- **FR-011**: When both the primary and fallback models fail, the prompt-generation endpoint MUST return a structured JSON error body with the same content-type used by the success path, rather than a plain-text body.
- **FR-012**: Selecting a saved prompt MUST copy its `description` field verbatim into the chat input textarea, with no variable/placeholder substitution performed.
- **FR-013**: The system MUST let a user create a new prompt with non-empty `name` and `description`, owned by that user. *(REQ-PROMPT-1)*
- **FR-014**: The system MUST let the owner, an admin, or a designated collaborator (per FR-001) edit an existing prompt's `name`/`description`. *(REQ-PROMPT-1)*
- **FR-015**: The system MUST let the owner or an admin permanently delete a prompt; a deleted prompt MUST NOT be returned by any subsequent list, read, or favorites operation, for any user. *(REQ-PROMPT-1)*
- **FR-016**: The system MUST support per-user favoriting and unfavoriting of any prompt the user has at least read access to, tracked independently of any other user's favorites and independently of the prompt's own ownership/sharing fields. *(REQ-PROMPT-2, favoriting half)*
- **FR-017**: Deleting a prompt MUST remove it from every user's favorites list, leaving no dangling references. *(REQ-PROMPT-1 / REQ-PROMPT-2)*
- **FR-018**: Selecting a prompt — whether from the prompt library directly or via any other entry point that references a prompt (e.g., a configured landing action, defined in a separate preferences spec) — MUST result in a chat populated with that prompt's content, satisfied by the copy-into-input mechanic defined in FR-012. This spec does not define how such entry points are configured. *(REQ-PROMPT-3)*

### Key Entities *(include if feature involves data)*

- **PromptModel**: `id`, `userId` (owner), `name` (title), `description` (the actual reusable prompt text — there is no separate `content`/`template` field), `sharedWith?`, `collaborators?`, `isPublished?` (legacy, superseded by `sharedWith`), `createdAt`. No template-variable/placeholder engine exists on this entity.
- **PromptFavorite**: `userId`, `promptIds[]` — per-user favorites list, mirroring the existing `Persona Favorite` entity; independent of a prompt's ownership/sharing state.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of transfer requests carrying forged `name`/`description`/`createdAt`/`sharedWith` values result in a transferred record that matches the pre-transfer server-side values for those fields, across a test corpus of forged-field variations.
- **SC-002**: 100% of simulated recreate-step failures during transfer leave the original prompt intact and unchanged under the original owner, with 0 permanent data-loss incidents in the test suite.
- **SC-003**: 100% of total prompt-generation failures (primary + fallback both failing) return a JSON-typed response matching the success path's content-type, across a test corpus of failure combinations.
- **SC-004**: 0 non-owner/non-admin/non-collaborator write requests succeed against the prompt write path in the authorization test suite.
- **SC-005**: 100% of create/edit/delete requests from authorized callers (owner/admin/collaborator per FR-001) succeed and are reflected in subsequent reads, across a CRUD test corpus.
- **SC-006**: 100% of delete operations remove the prompt from every list, read, and favorites surface (owner's, sharees', and any other user's), with 0 dangling references in the test suite.
- **SC-007**: 100% of favorite/unfavorite operations are reflected in the acting user's favorites list on the next read, with 0 instances of one user's favorite action appearing in another user's list.
- **SC-008**: 100% of prompt-selection actions populate the chat input with the prompt's `description` verbatim, with 0 substitution events, whether initiated from the library or another entry point.

## Assumptions

- Selecting a saved prompt copying `description` verbatim with no variable/placeholder substitution is an accepted, explicit limitation of this feature, not a bug to be fixed — `[bracket]` conventions in seed content remain purely cosmetic.
- `EnsurePromptOperation` returning a uniform ambiguous `UNAUTHORIZED` for both not-found and forbidden cases is intentional, matching the established persona-access pattern elsewhere in the codebase, and is retained as-is.
- "Atomic or recoverable" for ownership transfer does not require introducing a full distributed-transaction mechanism; a verify-before-delete / write-then-delete ordering, or an equivalent compensating-action approach, satisfies this requirement as long as failure never results in a lost or duplicated prompt.
- Sharing-permission rules (role-gating of sharing targets) are the canonical policy defined in spec 018 (Sharing & Permissions Policy) — this spec consumes that policy's `SharingDecision` rather than redefining role-gating rules itself; refactoring the current implementation onto spec 018 is tracked there, not here.
- Favoriting (Story 5 / FR-016-017) is a simple per-user list, mirroring the existing `Persona Favorite` entity; it carries no notification or ordering semantics beyond membership.
- Configuring a favorite prompt as a landing action (PRD §4.19, User Preferences) is out of scope for this spec — FR-018 defines only the prompt-selection-to-chat mechanic, not landing-action resolution or fallback behavior.
