# Feature Specification: Canvas Data-Access Tools & PII Redaction

**Feature Branch**: `008-canvas-data-access-pii-redaction`

**Created**: 2026-07-20

**Status**: Draft

**Input**: Derived from SSD_Document.md §3.4 (Canvas LTI Integration & Student View) — reframed from "as-is" discovery findings into target requirements. Source facts: a separate Canvas OAuth flow (`/api/canvas/oauth/*`) powers 16 read-only in-chat MCP tools for grades/assignments/courses, with tokens AES-256-GCM encrypted at rest and proactively refreshed 5 minutes before expiry; Canvas MCP tool output passes through a three-tier PII redaction pass (always-redact / peer-only-redact / always-strip-auth-fields) before reaching the LLM, failing closed by treating the caller as a "peer" (i.e. redacting) whenever the current user ID is missing; no roster/list-students MCP tool exists, by design, for FERPA compliance; Canvas identity for lesson submission is always session-derived, never client-supplied; the session `sub` is `hash(canvas_env:canvas_user_id)` to prevent numeric-ID collisions across the three Canvas tenants (ECPI/NTT/SkillOps) sharing one Cosmos partition space; Student View forces `isStudent:true` in both the `jwt()` and `session()` callbacks as defense-in-depth, and exiting requires a hard navigation so server components re-derive the un-downgraded role; and `/api/canvas/diagnose` has no explicit auth guard of its own, relying transitively on an internal helper throwing if unauthenticated, unlike every other route in this domain. LTI launch/session-minting mechanics and admin-impersonation-cookie issuance are out of scope here (see spec 003); general role-derivation logic is out of scope here (see spec 002). This pass additionally incorporates `docs/PRODUCT_REQUIREMENTS_DOCUMENT.md` §4.11 (Canvas/LTI Integration), which supplies REQ-LTI-3 (OAuth tokens encrypted at rest, refreshable, and health-checked — encryption and refresh are already covered by FR-002/FR-003, but the health-check guarantee was not yet specified and is added here as FR-013/SC-007, using the existing `/api/canvas/diagnose` route from Story 2 as the health-check surface), REQ-LTI-4 (a tool retrieving the student's course/assignment context to ground responses — already covered by the 16 read-only MCP tools in FR-001), and REQ-LTI-6 (no identity collision across multiple LMS environments — already covered by FR-010).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Chat tools surface Canvas grades/assignments/courses with PII protected before it reaches the model (Priority: P1)

A student or faculty member, chatting with a persona, asks a question that triggers a Canvas MCP tool (e.g., "what's my grade on assignment 3?"); the tool fetches live Canvas data and the response must reach the LLM with sensitive fields already redacted according to the caller's relationship to the data.

**Why this priority**: This is the core value of the feature — read-only Canvas data access inside chat — and it cannot ship without its redaction guarantee holding in every case, including when caller identity can't be determined. A tool that returns data but leaks PII, or that only redacts when things go right, is not shippable.

**Independent Test**: Invoke each of the 16 Canvas MCP tools with a caller who owns the underlying data, a caller who is a peer (e.g., viewing a shared course roster field), and a request where the current user ID is unresolvable; confirm always-redact fields are stripped in all three cases, peer-only-redact fields are stripped only for the peer case, auth fields (tokens/keys) are stripped in all cases, and the unresolvable-identity case redacts as if the caller were a peer.

**Acceptance Scenarios**:

1. **Given** a Canvas MCP tool call for the current user's own grade data, **When** the response is assembled for the LLM, **Then** always-redact fields and auth fields are stripped, and the current user's own non-restricted fields pass through.
2. **Given** a Canvas MCP tool call returning another student's peer-visible data, **When** the response is assembled for the LLM, **Then** peer-only-redact fields are also stripped.
3. **Given** a request where the current user ID cannot be determined, **When** any Canvas MCP tool output is assembled for the LLM, **Then** the system applies peer-level redaction (fails closed) rather than passing the data through unredacted.
4. **Given** any of the 16 read-only Canvas MCP tools, **When** invoked, **Then** it performs no write/mutating operation against Canvas.

---

### User Story 2 - Every Canvas data-access route enforces an explicit, consistent auth check (Priority: P1)

An unauthenticated or unauthorized caller hits any route under the Canvas data-access surface, including the diagnostic route `/api/canvas/diagnose`, expecting a uniform, explicit rejection rather than behavior that depends on an incidental internal throw.

**Why this priority**: Every other route in this domain checks the session explicitly; `/api/canvas/diagnose` today has no explicit guard of its own and only fails closed because an internal helper happens to throw when unauthenticated. That's an accidental protection, not a designed one, and a refactor of the helper could silently remove it. This is ranked with Story 1 because an unauthenticated caller reaching any Canvas data path — diagnostic or otherwise — undermines the redaction guarantee above.

**Independent Test**: Call `/api/canvas/diagnose` and every other Canvas data-access route with no session and confirm each rejects with the same explicit, consistent unauthenticated response, independent of any internal helper's behavior.

**Acceptance Scenarios**:

1. **Given** no active session, **When** a request is made to `/api/canvas/diagnose`, **Then** the route's own explicit auth check rejects it before any diagnostic logic runs.
2. **Given** no active session, **When** a request is made to any other route in the Canvas data-access domain, **Then** it is rejected with the same explicit-check pattern and equivalent response shape as `/api/canvas/diagnose`.
3. **Given** an active, valid session, **When** a request is made to `/api/canvas/diagnose`, **Then** the diagnostic logic runs normally.

---

### User Story 3 - Canvas OAuth access stays valid across a session without exposing tokens at rest (Priority: P2)

A user connects Canvas OAuth once and continues using Canvas-backed chat tools over a long session without re-authenticating, while the stored access/refresh tokens remain protected even if the underlying data store is compromised.

**Why this priority**: This is a reliability and defense-in-depth concern layered on top of Story 1 — the tools must keep working and stay protected, but a temporary token-refresh hiccup degrades convenience, not safety, so it ranks below the core redaction and auth guarantees.

**Independent Test**: Inspect the persisted token record directly in the data store and confirm it is not readable as plaintext; drive a token to within 5 minutes of expiry and confirm a refresh occurs before any tool call fails due to expiry.

**Acceptance Scenarios**:

1. **Given** a completed Canvas OAuth connection, **When** the token is persisted, **Then** it is stored AES-256-GCM encrypted, never in plaintext.
2. **Given** a stored token nearing expiry, **When** it reaches the 5-minutes-before-expiry threshold, **Then** the system proactively refreshes it before any tool call is made with it.
3. **Given** a refresh attempt that fails, **When** a Canvas MCP tool is subsequently invoked, **Then** the failure is surfaced as a distinct, actionable error rather than a silent stale-data response.
4. **Given** a connected Canvas OAuth account, **When** its token health is checked (e.g., via `/api/canvas/diagnose`), **Then** the system reports whether the connection is currently valid, refreshable, or requires reconnection, without needing a tool-call failure to discover this.

---

### User Story 4 - No Canvas tool ever exposes a student roster or list-students capability (Priority: P2)

A caller — student, faculty, or a future tool contribution — attempts to use or add a Canvas MCP tool that would enumerate other students in a course.

**Why this priority**: This is an intentional, load-bearing FERPA constraint rather than a gap to close, but it must be explicitly specified so it survives future tool additions; ranked P2 because it's a standing guarantee to protect rather than a new capability users are waiting on.

**Independent Test**: Enumerate the full set of available Canvas MCP tools and confirm none accepts a course/section identifier and returns a list of enrolled students; confirm this holds after any new tool is added to the set.

**Acceptance Scenarios**:

1. **Given** the full catalog of Canvas MCP tools, **When** enumerated, **Then** none returns a roster or list of students for a course/section.
2. **Given** a request shaped to request roster-like data from any existing tool, **When** it is made, **Then** the tool either declines or returns only data scoped to the requesting user, never a multi-student listing.

---

### User Story 5 - Canvas identity used for data access and submission is never accepted from the client (Priority: P2)

A request to a Canvas data-access or submission path includes a client-supplied Canvas user/course identifier that differs from the caller's actual session identity.

**Why this priority**: This closes an anti-forgery gap that would otherwise let a caller impersonate another Canvas identity; it's a correctness/security guarantee underlying Stories 1–3, ranked P2 because it is already enforced on the submission path and this spec's job is to hold the line consistently across all data-access paths too.

**Independent Test**: Issue a request to a Canvas data-access or submission endpoint with a client-supplied Canvas user/course ID that mismatches the session's own Canvas identity, and confirm the client-supplied value is discarded in favor of the session-derived identity.

**Acceptance Scenarios**:

1. **Given** a request body containing a Canvas user/course ID different from the session's own, **When** the request is processed, **Then** the session-derived identity is used exclusively and the client-supplied value has no effect.
2. **Given** two Canvas tenants (e.g., ECPI and NTT) that happen to share a numeric `canvas_user_id`, **When** each user's session is established, **Then** their derived identities (`hash(canvas_env:canvas_user_id)`) remain distinct and neither can access the other's data.

---

### User Story 6 - Admin Student View sessions cannot retain elevated Canvas data access (Priority: P3)

An admin enters Student View to preview the student experience, then exits; at every point, the effective role used to gate Canvas data-access tools must reflect the intended (downgraded, then restored) state, not a stale or partially-applied one.

**Why this priority**: This is defense-in-depth on top of the impersonation-cookie mechanism (covered elsewhere) — ranked P3 because it's a belt-and-suspenders guarantee against a single-point role-check bug, not the primary control.

**Independent Test**: Enter Student View as an admin and confirm both the `jwt()` and `session()` callbacks independently report `isStudent:true` for the duration; exit Student View and confirm the admin's true role is restored only after a hard navigation (full page load), not merely a client-side state change.

**Acceptance Scenarios**:

1. **Given** an admin has entered Student View, **When** either the `jwt()` or `session()` callback is invoked, **Then** each independently forces `isStudent:true` and all elevated flags `false`.
2. **Given** an admin exits Student View, **When** the exit action completes, **Then** it triggers a hard navigation, and server components re-derive the admin's un-downgraded role from a fresh request rather than trusting cached client state.

### Edge Cases

- What happens when the PII-classification pass cannot determine whether a field is always-redact vs. peer-only-redact (e.g., a new, unclassified Canvas field type)? The system must not default to pass-through.
- How does the system behave if a Canvas MCP tool call succeeds but the redaction step itself throws — must the tool call fail rather than return unredacted data?
- What happens when a token refresh occurs concurrently with an in-flight tool call using the pre-refresh token?
- How does `/api/canvas/diagnose` behave for a valid session belonging to a non-admin, non-staff user — is diagnostic output itself scoped/redacted the same way tool output is?
- What happens when a Student View admin's hard-navigation exit is interrupted (e.g., browser back button) before completing — does the downgraded state persist until a fresh request re-derives the role?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a Canvas OAuth connection flow, separate from LTI-launch session bootstrap, that authorizes 16 read-only MCP tools covering grades, assignments, and courses.
- **FR-002**: Canvas OAuth tokens MUST be encrypted at rest using AES-256-GCM.
- **FR-003**: Canvas OAuth tokens MUST be proactively refreshed at least 5 minutes before expiry, before being used for a tool call.
- **FR-004**: Before any Canvas MCP tool output reaches the LLM, the system MUST apply a three-tier PII redaction pass: always-redact fields, peer-only-redact fields, and always-strip auth fields.
- **FR-005**: WHEN the current user ID cannot be determined, THE SYSTEM SHALL apply peer-level redaction (fail closed) rather than passing data through unredacted.
- **FR-006**: The system MUST NOT expose any Canvas MCP tool capable of returning a roster or list of students for a course/section, and this constraint MUST hold for any future tool added to the set.
- **FR-007**: Every Canvas data-access route MUST perform its own explicit, session-based auth check before executing business logic, rather than relying transitively on an internal helper's incidental behavior.
- **FR-008**: `/api/canvas/diagnose` specifically MUST include the same explicit auth check pattern used by every other route in this domain.
- **FR-009**: Canvas identity used for data access and lesson submission MUST be derived exclusively from the server-side session; any client-supplied Canvas user/course identifier MUST be discarded.
- **FR-010**: The session identity (`sub`) MUST be computed as `hash(canvas_env:canvas_user_id)` so that numeric ID collisions across Canvas tenants sharing one Cosmos partition space cannot cause cross-tenant data access.
- **FR-011**: WHEN an admin enters Student View, THE SYSTEM SHALL force `isStudent:true` (and all elevated role flags `false`) independently in both the `jwt()` and `session()` callbacks.
- **FR-012**: Exiting Student View MUST require a hard navigation, so server components re-derive the admin's true role from a fresh request rather than from cached client-side state.
- **FR-013**: THE SYSTEM MUST provide a health-check capability for a connected Canvas OAuth account (e.g., via `/api/canvas/diagnose`) that reports whether the stored token is valid, refreshable, or requires the user to reconnect. *(PRD REQ-LTI-3)*

### Key Entities *(include if feature involves data)*

- **CanvasOAuthToken**: per-user, per-tenant encrypted access/refresh token pair for the Canvas data-access OAuth flow; independent of the LTI-launch session.
- **Canvas MCP Tool Output**: structured tool-call result subject to the three-tier redaction pass (always-redact / peer-only-redact / always-strip-auth-fields) before assembly into the LLM context.
- **Canvas Session Identity**: `sub = hash(canvas_env:canvas_user_id)`, scoped per tenant (ECPI/NTT/SkillOps) to prevent cross-tenant collisions in the shared Cosmos partition space.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of Canvas MCP tool responses reaching the LLM have always-redact and auth fields stripped, across all 16 tools, in a test corpus covering owner, peer, and unresolvable-identity callers.
- **SC-002**: 0 unredacted responses occur when current user ID is unresolvable, across a test corpus of at least all 16 tools.
- **SC-003**: 100% of unauthenticated requests to `/api/canvas/diagnose` and every other Canvas data-access route are rejected by an explicit auth check, with identical response shape across routes.
- **SC-004**: 0 Canvas MCP tools in the full enumerated catalog return multi-student roster data, verified on every catalog change.
- **SC-005**: 100% of requests carrying a client-supplied Canvas identity that mismatches the session are processed using the session-derived identity only, with 0 cross-tenant data-access successes in a collision test (matching numeric IDs across two tenants).
- **SC-006**: 100% of Student View sessions report `isStudent:true` from both `jwt()` and `session()` callbacks for the duration of impersonation, and 0 exits restore elevated access without a hard navigation.
- **SC-007**: 100% of Canvas OAuth health-check invocations in the test suite correctly report valid/refreshable/reconnect-required status without triggering a live tool-call failure.

## Assumptions

- The 16-tool catalog count reflects the current tool set; this spec's guarantees (redaction, no-roster, explicit auth) are intended to bind any future additions to that catalog, not just the present 16.
- LTI launch/session-minting and admin-impersonation-cookie issuance/audit-logging are covered by spec 003 and are not re-specified here; this spec assumes a valid session (Canvas-launched or Student-View-downgraded) already exists by the time these routes are reached.
- General AAD-group-based role derivation is covered by spec 002; this spec only concerns the Canvas-specific `isStudent` forcing during Student View.
- The underlying PII classification (which fields are always-redact vs. peer-only-redact) is maintained as configuration/data outside this spec; this spec requires the pass to run and to fail closed, not that any specific field list is exhaustive.
