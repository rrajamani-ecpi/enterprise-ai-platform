# Feature Specification: Prompt CRUD, Sharing & Ownership Transfer

**Feature Branch**: `016-prompt-crud-sharing-ownership-transfer`

**Created**: 2026-07-20

**Status**: Draft

**Input**: Derived from SSD_Document.md §3.9 (Domain: Prompt Library) — reframed from "as-is" discovery findings into target requirements. Source facts: `TransferPromptOwnerShip` trusts client-supplied JSON for `name`/`description`/`createdAt`/`sharedWith` instead of re-deriving those fields from the server-verified record (only ownership is actually checked); ownership transfer is implemented as delete-then-recreate under the new owner's partition key, with no rollback if the recreate fails; and total prompt-generator failure (primary + fallback model both fail) returns a plain-text 500 body inconsistent with the JSON content-type of the success path.

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

### Edge Cases

- What happens when a transfer request targets a new owner who does not exist or is not a valid recipient (e.g., malformed email/user identifier)?
- How does the system handle a transfer request submitted twice in quick succession (double-submit) against the same prompt?
- What happens when the primary model fails but the fallback model succeeds — does the response format match the JSON success/failure contract established here?
- How does `EnsurePromptOperation` behave when the prompt referenced in a transfer request was deleted or already transferred by another caller between page load and submission?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `EnsurePromptOperation` MUST grant write access only to admins, the prompt's owner, or its collaborators.
- **FR-002**: `EnsurePromptOperation` MUST grant read access additionally to anyone the prompt is `sharedWith` (individual email or group token).
- **FR-003**: The system MUST require non-empty `name` and non-empty `description` for prompt create/update operations.
- **FR-004**: Prompt sharing targets MUST be role-gated using the same sharing-permission rules already applied to personas.
- **FR-005**: The ownership-transfer operation MUST re-derive `name`, `description`, `createdAt`, `sharedWith`, and all other non-ownership fields from the server-side record at the time of transfer, and MUST discard any client-supplied values for those fields.
- **FR-006**: The ownership-transfer operation MUST authorize the caller (owner or admin) before performing any write, consistent with existing behavior.
- **FR-007**: The ownership-transfer operation MUST be atomic or recoverable: if the write under the new owner's partition fails, the original record under the original owner MUST remain intact and the operation MUST report failure rather than leaving the prompt in a lost or duplicated state.
- **FR-008**: On successful transfer, the prompt MUST exist exactly once, under the new owner, and no longer under the original owner.
- **FR-009**: `EnsurePromptOperation` failures MUST continue to return a uniform, ambiguous `UNAUTHORIZED` result regardless of whether the true cause is "not found" or "forbidden," consistent with the equivalent persona-access pattern.
- **FR-010**: AI-assisted prompt generation MUST wrap user input in the existing fixed prompt-engineering meta-prompt and call a primary model, falling back once to a secondary model on primary failure.
- **FR-011**: When both the primary and fallback models fail, the prompt-generation endpoint MUST return a structured JSON error body with the same content-type used by the success path, rather than a plain-text body.
- **FR-012**: Selecting a saved prompt MUST copy its `description` field verbatim into the chat input textarea, with no variable/placeholder substitution performed.

### Key Entities *(include if feature involves data)*

- **PromptModel**: `id`, `userId` (owner), `name` (title), `description` (the actual reusable prompt text — there is no separate `content`/`template` field), `sharedWith?`, `collaborators?`, `isPublished?` (legacy, superseded by `sharedWith`), `createdAt`. No template-variable/placeholder engine exists on this entity.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of transfer requests carrying forged `name`/`description`/`createdAt`/`sharedWith` values result in a transferred record that matches the pre-transfer server-side values for those fields, across a test corpus of forged-field variations.
- **SC-002**: 100% of simulated recreate-step failures during transfer leave the original prompt intact and unchanged under the original owner, with 0 permanent data-loss incidents in the test suite.
- **SC-003**: 100% of total prompt-generation failures (primary + fallback both failing) return a JSON-typed response matching the success path's content-type, across a test corpus of failure combinations.
- **SC-004**: 0 non-owner/non-admin/non-collaborator write requests succeed against the prompt write path in the authorization test suite.

## Assumptions

- Selecting a saved prompt copying `description` verbatim with no variable/placeholder substitution is an accepted, explicit limitation of this feature, not a bug to be fixed — `[bracket]` conventions in seed content remain purely cosmetic.
- `EnsurePromptOperation` returning a uniform ambiguous `UNAUTHORIZED` for both not-found and forbidden cases is intentional, matching the established persona-access pattern elsewhere in the codebase, and is retained as-is.
- "Atomic or recoverable" for ownership transfer does not require introducing a full distributed-transaction mechanism; a verify-before-delete / write-then-delete ordering, or an equivalent compensating-action approach, satisfies this requirement as long as failure never results in a lost or duplicated prompt.
- Sharing-permission rules (role-gating of sharing targets) are reused from the existing persona sharing model rather than redefined here.
