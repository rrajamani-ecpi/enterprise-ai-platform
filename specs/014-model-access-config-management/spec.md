# Feature Specification: Model & Access Configuration Management

**Feature Branch**: `014-model-access-config-management`

**Created**: 2026-07-20

**Status**: Draft

**Input**: Derived from SSD_Document.md §3.8 (Admin / Settings / Executive Reporting) — reframed from "as-is" discovery findings into target requirements, scoped to the admin-gate, model-config, message-limit, persona-generation-model, and user-preferences material only (executive dashboard reporting is covered by a separate spec). Source facts: an admin-only gate is consistently applied across system-config, model-config, message-limit, and persona-generation-model mutation services; model deletion is always soft, never hard; model read access for chat is computed by intersecting `isEnabled`, the caller's role allow-list, and (if `requiresAdvancedModelAccess`) the caller's `advancedModelAccess` flag; message-limit and persona-generation-model *read* paths are intentionally not admin-gated while their write/admin-UI paths are, a documented asymmetry; message-limit caps are re-validated server-side as integers ≥1 regardless of client-side validation; persona-generation model selection is restricted to a pre-filtered allow-list even if a broader model exists in the general registry; `/api/user/preferences/*` routes currently have no explicit session check of their own, relying on an internal helper throwing if unauthenticated, which produces a 500 instead of the 401 every other route in this domain returns.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Admin-only gate protects every config mutation (Priority: P1)

An admin changes system-wide model configuration, message-limit policy, or persona-generation-model policy; a non-admin attempts the same mutations directly against the API.

**Why this priority**: This is the foundational control for the whole domain. System-config, model-config, message-limit, and persona-generation-model documents govern what every user in the system can do — if the admin gate is missing or inconsistent on even one of these mutation surfaces, any authenticated user could change which models are available, raise or lower message limits, or alter persona-generation policy for everyone.

**Independent Test**: As a non-admin, call each of the system-config, model-config, message-limit, and persona-generation-model mutation endpoints directly and confirm every one is rejected before any write occurs; as an admin, confirm the same calls succeed.

**Acceptance Scenarios**:

1. **Given** a non-admin, authenticated caller, **When** they call the model-config, system-config, message-limit, or persona-generation-model mutation endpoint directly, **Then** the request is rejected and no document is changed.
2. **Given** an admin caller, **When** they call any of the same mutation endpoints, **Then** the request succeeds and the underlying document is updated.

---

### User Story 2 - Effective model access is computed correctly, and model removal is always reversible (Priority: P1)

A user opens chat and the system determines which models they may actually select; an admin retires a model from the registry.

**Why this priority**: This is the actual enforcement mechanism behind the admin gate in Story 1 — a wrong intersection either leaks advanced/premium models to users who shouldn't have them or blocks legitimate users, and a hard delete of a model config would break any historical reference to it (threads, personas) that still name that model id.

**Independent Test**: Configure a model with `requiresAdvancedModelAccess: true`, enabled, and present in one role's allow-list but not another's; verify computed access matches expectation for a user with and without `advancedModelAccess` in each role. Separately, delete a model via the admin UI and confirm the underlying `ModelConfigDocument` still exists with `isDeleted: true` rather than being removed.

**Acceptance Scenarios**:

1. **Given** a model that is enabled, in the caller's role allow-list, and does not require advanced access, **When** the caller's available models are computed, **Then** the model is included.
2. **Given** a model that requires advanced access, **When** a caller without `advancedModelAccess` requests their available models, **Then** the model is excluded even if it is enabled and in their role allow-list.
3. **Given** a model that is disabled (`isEnabled: false`) or absent from the caller's role allow-list, **When** available models are computed, **Then** the model is excluded regardless of the advanced-access flag.
4. **Given** an admin deletes a model, **When** the deletion completes, **Then** the model's document persists in storage with `isDeleted: true` and is never physically removed.

---

### User Story 3 - Message-limit and persona-generation-model configs stay readable to all users while remaining write-protected (Priority: P2)

Any authenticated user's chat request needs to evaluate the current message-limit policy and, separately, the persona-generation feature needs to evaluate the current allowed-model list — neither of these callers is an admin.

**Why this priority**: This is a documented, intentional asymmetry, not an oversight: message-limit enforcement and persona-generation model selection run inside request paths triggered by every user, not just admins, so the read side must stay open for enforcement to work app-wide, while the write/admin-UI side must stay admin-gated per Story 1. Both halves must hold together or the feature breaks either for ordinary users or for security.

**Independent Test**: As a non-admin, successfully GET the effective message-limit config and the effective persona-generation-model config; then attempt to write to either as the same non-admin and confirm rejection; confirm an admin can still write to both.

**Acceptance Scenarios**:

1. **Given** a non-admin, authenticated caller, **When** they read the effective message-limit configuration, **Then** the read succeeds.
2. **Given** a non-admin, authenticated caller, **When** they read the effective persona-generation-model configuration, **Then** the read succeeds.
3. **Given** a non-admin, authenticated caller, **When** they attempt to write to either configuration, **Then** the request is rejected.
4. **Given** an admin caller, **When** they write to either configuration, **Then** the write succeeds.

---

### User Story 4 - Config mutations are re-validated server-side regardless of client input (Priority: P2)

A direct API caller (bypassing the admin UI's own client-side validation) submits a message-limit cap of `0` or a non-integer, or submits a persona-generation model id that exists in the general model registry but not the persona-generation allow-list.

**Why this priority**: Client-side validation on the admin forms is real but not authoritative; a cap of `0` would block every user's messages system-wide, and an out-of-allow-list model choice for persona generation would bypass a deliberate curation step. Server-side re-validation is the only thing actually preventing these outcomes, so this ranks just below the gate and computation correctness in Stories 1–2 but above the lower-severity fix in Story 5.

**Independent Test**: Submit a message-limit cap of `0`, a negative number, or a non-integer via direct API call and confirm rejection with no config change; submit a persona-generation model id that is present in the general model registry but absent from the persona-generation allow-list and confirm rejection.

**Acceptance Scenarios**:

1. **Given** a direct API request setting a message-limit cap to `0`, a negative number, or a non-integer, **When** it is submitted, **Then** the system rejects it and the previous cap remains in effect.
2. **Given** a direct API request setting a message-limit cap to a positive integer ≥1, **When** it is submitted, **Then** the system accepts it.
3. **Given** a direct API request selecting a persona-generation model that exists in the general model registry but not the persona-generation allow-list, **When** it is submitted, **Then** the system rejects it.
4. **Given** a direct API request selecting a persona-generation model that is on the allow-list, **When** it is submitted, **Then** the system accepts it.

---

### User Story 5 - User-preference routes return a correct 401 for unauthenticated requests (Priority: P3)

An unauthenticated (or session-expired) request hits any `/api/user/preferences/*` route.

**Why this priority**: The unauthenticated request is already blocked today — an internal helper throws before any data is touched — so there is no security gap here. The problem is purely a response-shape inconsistency (500 instead of 401), which is lower severity than the correctness and access-control issues in Stories 1–4 but still worth fixing for consistent client/monitoring behavior.

**Independent Test**: Call any `/api/user/preferences/*` route with no session and confirm a 401 response with a body consistent with the unauthenticated-error shape used on admin routes elsewhere in this domain, not a 500.

**Acceptance Scenarios**:

1. **Given** no active session, **When** a caller requests any `/api/user/preferences/*` route, **Then** the response is 401, not 500.
2. **Given** an active, valid session, **When** a caller requests the same route, **Then** the request proceeds normally (unaffected by this change).

### Edge Cases

- What happens when a model that is soft-deleted is still referenced by an existing persona or chat thread — does model-access computation exclude it from *future* selection while historical references still resolve for display?
- What happens when a caller has `advancedModelAccess: true` but the model's role allow-list itself excludes their role — does the advanced-access flag ever override a missing role entry? (It must not; both conditions are required, not either/or.)
- What happens when the persona-generation-model allow-list is empty or misconfigured — does persona generation fail closed with an actionable error rather than falling back to the general registry?
- What happens when a message-limit or persona-generation-model document does not yet exist for a fresh tenant — does the read path return sensible defaults rather than an error, so the "always readable" guarantee in Story 3 holds even before an admin has ever saved config?
- Does the 401 fix in Story 5 apply uniformly to every sub-route under `/api/user/preferences/*` (read and write), or only some?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST require the caller to be an admin before applying any mutation to system-config, model-config, message-limit, or persona-generation-model documents.
- **FR-002**: Model deletion MUST be implemented as a soft delete (`isDeleted` flag) only; a model's underlying document MUST never be physically removed.
- **FR-003**: The system MUST compute a caller's effective set of available chat models as the intersection of: the model being enabled (`isEnabled`), the model being present in the caller's role allow-list, and — only when the model has `requiresAdvancedModelAccess: true` — the caller having `advancedModelAccess`.
- **FR-004**: The message-limit configuration read path MUST remain accessible to any authenticated user, regardless of admin status.
- **FR-005**: The persona-generation-model configuration read path MUST remain accessible to any authenticated user, regardless of admin status.
- **FR-006**: The message-limit configuration write/admin-UI path MUST remain admin-gated.
- **FR-007**: The persona-generation-model configuration write/admin-UI path MUST remain admin-gated.
- **FR-008**: Message-limit caps MUST be re-validated server-side as integers ≥1 on every write, regardless of the submitted value's client-side validation state.
- **FR-009**: Persona-generation model selection MUST be restricted server-side to a pre-filtered allow-list on every write, even when a broader, otherwise-valid model exists in the general model registry.
- **FR-010**: Every route under `/api/user/preferences/*` MUST perform its own explicit authentication check and return 401 for an unauthenticated caller, rather than relying solely on an internal helper's thrown exception producing a different status.

### Key Entities *(include if feature involves data)*

- **SystemModelConfig**: admin singleton holding per-role model allow-lists (`roleModelAccess.{admin,staff,faculty,student,default}`, each `string[]` or `"*"`) plus embedding/image/artifact/fallback model selections.
- **ModelConfigDocument**: per-model registry entry — `id` (model id), `provider`, feature flags including `requiresAdvancedModelAccess`, `contextWindowSize`, pricing, `isEnabled`, `isDeleted`.
- **ModelAliasDocument**: maps a retired model id forward to its replacement.
- **Message-limit config**: per-message character cap and daily message cap, each independently toggleable, with server-enforced integer ≥1 validation on write.
- **Persona-generation-model config**: the pre-filtered allow-list of models eligible for AI-assisted persona/prompt generation, distinct from (and a subset of) the general model registry.
- **UserModel** (session-derived): supplies the `isAdmin` and `advancedModelAccess` flags and role membership used throughout this spec's access computations.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of non-admin mutation requests against system-config, model-config, message-limit, and persona-generation-model endpoints are rejected in the authorization test suite; 100% of equivalent admin requests succeed.
- **SC-002**: 0 mismatches between expected and computed model-access results across a test matrix spanning role × `isEnabled` × `requiresAdvancedModelAccess` × `advancedModelAccess` combinations.
- **SC-003**: 100% of models deleted via the admin UI remain present in storage with `isDeleted: true`; 0% are physically removed, across a test corpus.
- **SC-004**: 100% of non-admin read requests to the message-limit and persona-generation-model config endpoints succeed; 100% of non-admin write requests to the same endpoints are rejected.
- **SC-005**: 100% of direct-API message-limit-cap submissions that are `<1` or non-integer are rejected server-side; 100% of persona-generation-model submissions outside the allow-list are rejected — independent of client-side validation state.
- **SC-006**: 100% of unauthenticated requests to `/api/user/preferences/*` routes return 401; 0% return 500.

## Assumptions

- Executive dashboard reporting (usage analytics, growth metrics, board export) is explicitly out of scope for this spec and is covered separately, even though it shares §3.8's source material.
- The role → allow-list resolution order (env override → Cosmos config → defaults → `"*"`) described elsewhere in this codebase is retained unchanged; this spec covers the correctness of the intersection computation, not the allow-list resolution mechanism itself.
- The internal helper that throws on unauthenticated access to `/api/user/preferences/*` is retained as the underlying enforcement; this spec requires an explicit route-level check that maps the unauthenticated case to 401 before or alongside that helper, matching the pattern already used by admin routes, without mandating a specific implementation.
- "Advanced model access" (`advancedModelAccess`) remains a flag on `UserModel` sourced from session/AD-derived data; this spec does not change how that flag itself is granted.
