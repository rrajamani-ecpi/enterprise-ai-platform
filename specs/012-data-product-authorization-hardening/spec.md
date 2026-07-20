# Feature Specification: Data Product Authorization Hardening

**Feature Branch**: `012-data-product-authorization-hardening`

**Created**: 2026-07-20

**Status**: Draft

**Input**: Derived from SSD_Document.md §3.7 (Data Products) and §5 (Architectural Debt) — reframed from "as-is" discovery findings into target requirements. Source facts: `DeleteDataProductURL`/`UpdateDataProductURL` perform no authorization check at all before mutating (any authenticated caller holding a valid ID can edit or delete another user's URL entry); four separate, subtly divergent implementations of "can this user act on this data product" exist across the codebase (plain-email-comparison variants vs. a hashed-comparison variant); the external MCP endpoint returns all business failures as HTTP 200 differentiated only by message text; and `GetDataProductFile` returns raw `Response` objects instead of the app-wide `ServerActionResponse` envelope. This spec covers CRUD/authorization/visibility/sharing/MCP-endpoint behavior only — the Azure Logic Apps ingestion pipeline is out of scope (covered by a separate spec).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - URL entry mutations are rejected for unauthorized callers (Priority: P1)

A caller who is neither the data product's owner, a collaborator, nor an admin obtains a valid URL-entry ID (e.g., by guessing, log exposure, or a prior legitimate share) and calls the edit or delete endpoint directly.

**Why this priority**: `DeleteDataProductURL`/`UpdateDataProductURL` currently perform no authorization check at all — any authenticated caller holding a valid ID can edit or delete another user's URL entry. This is a live, exploitable data-integrity and confidentiality hole today, not a theoretical one, and must be closed before anything else in this domain is considered shippable.

**Independent Test**: Call the URL-entry update/delete endpoints directly as a user who is not the data product's owner, a collaborator, or an admin, and confirm the request is rejected before any record is changed or removed.

**Acceptance Scenarios**:

1. **Given** a URL entry belonging to a data product owned by User A, **When** User B (non-admin, non-owner, non-collaborator) issues a direct API update request against that entry, **Then** the system rejects it and no field is changed.
2. **Given** a URL entry belonging to a data product owned by User A, **When** User B issues a direct API delete request against that entry, **Then** the system rejects it and the entry still exists.
3. **Given** a collaborator on the data product, **When** they update a URL entry, **Then** the operation succeeds; **When** they attempt to delete it, **Then** the system rejects it (delete remains owner/admin-only).
4. **Given** an admin caller, **When** they update or delete any user's URL entry, **Then** the operation succeeds.

---

### User Story 2 - One canonical authorization check governs every data-product action (Priority: P1)

A developer adds or audits any data-product code path (CRUD, file upload, MCP access) that needs to answer "can this caller act on this data product."

**Why this priority**: Four separate, subtly divergent implementations of this check currently exist — some comparing plain emails, one comparing hashed identifiers. Divergence between them is exactly how a gap like Story 1 goes unnoticed elsewhere: a code path that calls the "wrong" variant, or none at all, silently drifts out of sync with the others. Consolidating to one implementation is inseparable from actually trusting Story 1's fix, so it is ranked equally P1.

**Independent Test**: Audit every server-side entry point that mutates or reads a data product (create, update, delete, URL edit/delete, file upload, MCP tool invocation) and confirm each one calls the same shared authorization function rather than a locally re-implemented comparison.

**Acceptance Scenarios**:

1. **Given** the codebase after this change, **When** any two data-product entry points are compared for their "can this user act on this data product" logic, **Then** both resolve through the same shared function with identical inputs/outputs for the same caller and resource.
2. **Given** a caller who is an owner, collaborator, admin, or none of these, **When** the canonical check is evaluated for that caller against a given data product, **Then** it returns a result consistent with the visibility/edit/delete rules in this spec for every call site, with no call site producing a different answer for the same caller/resource pair.

---

### User Story 3 - Visibility, edit, and delete scope are enforced consistently (Priority: P2)

A user views their data-product list, or acts on a specific data product as an owner, collaborator, or sharedWith recipient.

**Why this priority**: This behavior is already correct today, but it depends on the same divergent checks being consolidated in Story 2 — codifying it as an explicit requirement guards against regression once the checks are unified, and it is lower urgency than closing the active mutation hole in Story 1.

**Independent Test**: As an owner, a collaborator, an individual sharedWith recipient, a group-token (`@employees`/`@contractors`/`@{organizationId}`) sharedWith recipient, and an unrelated user, attempt to view, edit, and delete a data product, and confirm each sees exactly the access level defined for their role.

**Acceptance Scenarios**:

1. **Given** a data product with an owner, one collaborator, and one individual `sharedWith` email, **When** each of those three users lists visible data products, **Then** all three see it; an unrelated user does not.
2. **Given** a data product shared with a group token (e.g., `@employees`), **When** a user who is a member of that group (by role) requests it, **Then** they see it; a user outside the group does not.
3. **Given** a collaborator, **When** they edit the data product's name/description or upload a file, **Then** the operation succeeds; **When** they attempt to delete the data product itself, **Then** the system rejects it.
4. **Given** any create or update of a data product, **When** `name` or `description` is empty, **Then** the system rejects the request before persistence.
5. **Given** a delete of a data product, **When** the operation succeeds, **Then** the record is soft-deleted (marked `isDeleted`), never physically removed.

---

### User Story 4 - MCP endpoint distinguishes access failures from tool-level failures via HTTP status (Priority: P2)

An external MCP client calls the data-product MCP endpoint with a missing product ID, a disabled-MCP product, an invalid API key, or a request that triggers an in-tool error.

**Why this priority**: Today all four failure modes return HTTP 200, differentiated only by message text — a client that checks status codes (the normal integration pattern) cannot distinguish "you're not authorized to be here at all" from "you reached the tool and it failed." This is a real gap for API-key/availability failures, which occur before the MCP protocol layer is even engaged, but it is scoped below Stories 1-3 because it affects an external read-mostly integration surface rather than in-app data mutation.

**Independent Test**: Call the MCP endpoint with (a) a missing/deleted product ID, (b) MCP disabled on an otherwise-valid product, (c) an invalid API key, and (d) a valid request that causes an in-tool execution error, and confirm the HTTP status differs appropriately between the access-boundary failures and the in-tool failure.

**Acceptance Scenarios**:

1. **Given** an invalid or missing API key, **When** the MCP endpoint is called, **Then** the response is a non-2xx HTTP status (401/403) rather than 200.
2. **Given** a missing, deleted, or MCP-disabled product, **When** the MCP endpoint is called with a valid key, **Then** the response is a non-2xx HTTP status (404/403) rather than 200.
3. **Given** a valid key and an MCP-enabled product, **When** an in-tool operation fails (e.g., the underlying search errors), **Then** the response remains HTTP 200 with MCP-conventional error content in the tool result, consistent with MCP clients that parse success/failure from structured tool-result content rather than transport status for in-protocol errors.

---

### User Story 5 - File retrieval uses the standard response envelope (Priority: P3)

A server-side caller invokes `GetDataProductFile` the same way it would invoke any other data-product action.

**Why this priority**: This is a consistency/maintainability gap, not a security or correctness one — `GetDataProductFile` returning a raw `Response` instead of `ServerActionResponse<T>` is surprising to callers used to the app-wide convention, but it doesn't expose data incorrectly today. Lowest priority in this spec.

**Independent Test**: Call `GetDataProductFile` for an existing file and for a missing/unauthorized one, and confirm both results are shaped as `ServerActionResponse<T>` rather than a raw `Response` object.

**Acceptance Scenarios**:

1. **Given** a file the caller is authorized to read, **When** `GetDataProductFile` is called, **Then** it returns `{status:"OK", response:T}` matching the shape used by other data-product actions.
2. **Given** a file the caller is not authorized to read (or that doesn't exist), **When** `GetDataProductFile` is called, **Then** it returns the standard `{status:"UNAUTHORIZED"|"NOT_FOUND", errors:[...]}` shape rather than an ad hoc plain-text `Response`.

### Edge Cases

- What happens when a collaborator (edit rights, no delete rights) calls the URL-entry delete endpoint directly? Must be rejected under the same rule that blocks data-product-level delete for collaborators (Story 1, Scenario 3 / Story 3, Scenario 3).
- What happens when the canonical authorization check (Story 2) is evaluated for a caller who is a member of a `sharedWith` group token but not individually named? Must resolve identically to the existing group-token visibility rule (Story 3, Scenario 2) — consolidation must not silently narrow existing legitimate access.
- There is no `@students`/`isStudent` sharing branch for data products — a student can reach a data product only indirectly via a persona's configured `dataProducts` list, never through direct data-product-level sharing. This is treated as an explicit scope limitation to preserve, not a bug (see Assumptions).
- File-upload extraction failures being swallowed (upload still reports success) is a separate ingestion-reliability concern, not an authorization one, and is out of scope for this spec.
- What happens when an MCP call supplies a valid key for a product that exists but belongs to a different, unrelated caller with no sharing relationship? Must return the same non-2xx access-boundary failure as a missing product (no existence-confirming side channel).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST verify the caller is the data product's owner, a collaborator, or an admin before executing any URL-entry update, and reject the request otherwise with no field changed.
- **FR-002**: The system MUST verify the caller is the data product's owner or an admin before executing any URL-entry delete, and reject the request otherwise with the entry unchanged (collaborators are not sufficient for delete).
- **FR-003**: All data-product authorization decisions (create, update, delete, URL-entry update/delete, file upload, MCP access) MUST route through one canonical, shared authorization function rather than independently re-implemented comparison logic.
- **FR-004**: The canonical authorization function MUST use a single, consistent identity-comparison method across all call sites (not a mix of plain-email and hashed comparisons).
- **FR-005**: Data-product create and update MUST be schema-validated (non-empty `name` and `description`) before persistence.
- **FR-006**: Data-product delete MUST remain soft-delete only (`isDeleted` flag), never a physical removal.
- **FR-007**: Data-product visibility MUST be the union of owner, collaborators, and `sharedWith` entries (individual emails or `@employees`/`@contractors`/`@{organizationId}` group tokens).
- **FR-008**: Collaborators MUST be able to edit a data product's fields and upload files to it, but MUST NOT be able to delete the data product itself; delete MUST remain owner/admin-only.
- **FR-009**: File upload MUST require edit access (owner, collaborator, or admin) to the target data product.
- **FR-010**: The MCP endpoint MUST return a non-2xx HTTP status for access-boundary failures (invalid/missing API key, missing/deleted/MCP-disabled product) rather than HTTP 200.
- **FR-011**: The MCP endpoint MUST continue returning HTTP 200 with MCP-conventional error content for in-protocol tool-execution failures on an otherwise-valid, authorized request.
- **FR-012**: `GetDataProductFile` MUST return `ServerActionResponse<T>` for both success and failure cases, consistent with every other data-product server action.

### Key Entities *(include if feature involves data)*

- **DataProductModel**: `id`, `userId` (owner), `name`/`description` (required non-empty), `isDeleted` (soft delete), `collaborators?`, `sharedWith?` (individual emails or group tokens), `apiKey?` (MCP credential), `mcpEnabled?`.
- **DataProductDocumentModel**: an uploaded file/URL entry belonging to a data product (`id`, `dataProductId`, `fileName`/`url`); the entity targeted by the URL-entry update/delete authorization fix.
- **Canonical Authorization Check**: the single shared function (replacing the four divergent implementations) that answers "can this caller view / edit / delete this data product," consumed by every CRUD, file-upload, and MCP entry point.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 0 non-owner/non-collaborator/non-admin URL-entry update or delete requests succeed against the direct API in the authorization test suite.
- **SC-002**: 0 collaborator-issued data-product delete requests (data-product-level or URL-entry-level) succeed.
- **SC-003**: 100% of data-product server-side entry points (CRUD, URL-entry CRUD, file upload, MCP access) resolve authorization through the single canonical function, verified by code audit with 0 remaining independent comparison implementations.
- **SC-004**: 100% of MCP access-boundary failures (bad key, missing/disabled product) return a non-2xx HTTP status in the test corpus; 100% of in-tool execution failures on valid/authorized requests continue returning HTTP 200.
- **SC-005**: 100% of `GetDataProductFile` responses conform to the `ServerActionResponse<T>` envelope shape.

## Assumptions

- The absence of an `@students`/`isStudent` sharing branch for data products is an intentional scope limitation to preserve, not a bug: students are expected to reach data products only through a persona's configured `dataProducts` list (enforced server-side in Chat Core), not via direct data-product sharing. This spec does not add a student sharing branch.
- "Collaborator" and "sharedWith" semantics reuse the same sharing-permission model already established for personas/prompts elsewhere in the codebase, rather than introducing a new one.
- The MCP HTTP-status fix (Story 4) draws a line between pre-protocol access-boundary failures (auth/existence, which move to real HTTP status codes) and in-protocol tool-execution failures (which stay HTTP 200 per MCP client conventions that parse success/failure from structured tool-result content, not transport status). This is judged the correct target behavior rather than treating the current all-200 behavior as an accepted protocol convention, because API-key rejection and product-not-found happen before any MCP tool logic runs and are ordinary HTTP authorization/resource concerns.
- File-upload extraction-failure-swallowed-as-success and the Azure Logic Apps ingestion pipeline are explicitly out of scope for this spec; they belong to a separate ingestion-pipeline spec.
- Ownership-transfer mechanics (if any exist for data products, by analogy to personas/prompts) are out of scope here; this spec addresses per-request authorization checks, not transfer workflows.
