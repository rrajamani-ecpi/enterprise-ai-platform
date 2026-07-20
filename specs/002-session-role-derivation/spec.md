# Feature Specification: Session Resolution & Role Derivation

**Feature Branch**: `002-session-role-derivation`

**Created**: 2026-07-20

**Status**: Draft

**Input**: Derived from SSD_Document.md §3.1 (Auth & Identity) — reframed from "as-is" discovery findings into target requirements, scoped strictly to session-resolution, role-derivation, and token-refresh behavior. Source facts: the `impersonateAsStudent` role-downgrade is independently re-implemented in three separate places (`jwt()` callback, `session()` callback, `userSession()`), `isContractor` is permanently hardcoded `false` with no AD group configured for it, `getCurrentUser()` with no session throws a raw `Error("User not found")` inconsistent with the structured `ServerActionResponse` envelope used elsewhere, and AAD token-refresh failure sets `session.error="RefreshAccessTokenError"` with no automatic sign-out. Canvas-LTI launch/bootstrap and admin Student-View impersonation-initiation flows are explicitly out of scope — covered by a separate spec.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Impersonation downgrade is enforced from a single source of truth (Priority: P1)

An admin is in Student View (`impersonateAsStudent === true`). Every code path that resolves the current user's role — the JWT callback, the session callback, and any server-side `userSession()` lookup — must agree on the downgraded role, with no possibility of one path forgetting to apply it.

**Why this priority**: Today the downgrade (force all elevated role flags to `false`, `isStudent` to `true`) is independently re-implemented in three places. Any future change to role logic that updates only one or two of these sites silently reopens a privilege-leak window — an admin's elevated flags could survive impersonation in whichever code path was missed. This is the highest-severity risk in the domain because it's a security property (role downgrade), not just a UX inconsistency, and every other session-resolution behavior sits downstream of it.

**Independent Test**: With impersonation active, independently invoke the JWT-callback path, the session-callback path, and a server-side `userSession()`/`getCurrentUser()` call, and confirm all three return identical, fully-downgraded role flags — then repeat after modifying the shared downgrade logic once and confirm the change is reflected in all three without touching call-site code.

**Acceptance Scenarios**:

1. **Given** `impersonateAsStudent === true` for the current session, **When** the JWT callback, session callback, and `userSession()` are each invoked, **Then** all three report `isAdmin=false`, `isEmployee=false`, `isContractor=false`, `isStudent=true`.
2. **Given** the shared downgrade logic is changed in one place (e.g., an additional flag is added to `UserModel`), **When** the same three call paths are invoked under impersonation, **Then** all three reflect the new flag as downgraded without any of the three call sites being edited individually.
3. **Given** `impersonateAsStudent` is not set (normal session), **When** the same three call paths are invoked, **Then** none of them apply the downgrade and the user's actual role flags are returned unchanged.

---

### User Story 2 - Missing-session lookups return a structured error, not a raw exception (Priority: P2)

A server-side code path calls `getCurrentUser()` when no session exists (e.g., a background job, a misconfigured route, or a race during sign-out).

**Why this priority**: Today this throws a raw `Error("User not found")`, which callers must catch ad hoc — inconsistent with the `ServerActionResponse` envelope (`{status:"OK"|"ERROR"|"UNAUTHORIZED", ...}`) used almost universally elsewhere in the codebase for expected failure modes. This is a correctness/consistency gap rather than a security hole, so it ranks below the downgrade-enforcement risk in Story 1, but it directly affects how reliably every other domain's server actions can handle "no session" as an expected, structured outcome rather than an uncaught exception.

**Independent Test**: Call `getCurrentUser()` (or the shared session-resolution helper it wraps) with no active session and confirm the caller receives the app's standard structured error/response shape rather than an unhandled thrown `Error`.

**Acceptance Scenarios**:

1. **Given** no active session, **When** `getCurrentUser()` is invoked, **Then** the result is a structured `UNAUTHORIZED`/`ERROR` response consistent with `ServerActionResponse`, not a thrown generic `Error`.
2. **Given** a caller that already handles `ServerActionResponse`-shaped results elsewhere, **When** it calls `getCurrentUser()` with no session, **Then** it can branch on the response the same way it branches on any other domain's "no access" result, without a special try/catch just for this call.
3. **Given** an active, valid session, **When** `getCurrentUser()` is invoked, **Then** it continues to return the resolved `UserModel` unchanged.

---

### User Story 3 - Token-refresh failure gives the user a clear path back to a valid session (Priority: P2)

A signed-in user's AAD access token expires and the automatic refresh attempt fails (e.g., the refresh token itself has expired or was revoked).

**Why this priority**: Today this only sets `session.error="RefreshAccessTokenError"` with no automatic sign-out or forced re-authentication — a user can be left indefinitely in a session that looks active but silently fails on the next AAD-dependent call. This is ranked alongside Story 2: it's a real gap, but it degrades gracefully today (the user isn't granted anything they shouldn't have) rather than being an active privilege risk.

**Why this priority (severity note)**: Left as a client-only flag, a user could sit on a broken session for an extended period before noticing (e.g., only when a downstream Azure AD call fails) rather than being prompted to re-authenticate at the moment refresh fails.

**Independent Test**: Force a refresh-token failure for an active session and confirm the user is presented with an explicit re-authentication prompt (rather than continuing to interact with a session carrying only an internal error flag) on the next client interaction after the failure.

**Acceptance Scenarios**:

1. **Given** an active session whose token refresh fails, **When** the client next checks session state, **Then** the user is shown a clear re-authentication prompt (e.g., redirected to sign-in) rather than silently continuing with `session.error` set and no visible signal.
2. **Given** a user re-authenticates after a refresh failure, **When** sign-in completes, **Then** a fresh, valid session replaces the broken one with correctly re-derived role flags.
3. **Given** a session whose token refresh succeeds, **When** the client checks session state, **Then** no re-authentication prompt is shown and the session continues normally.

---

### User Story 4 - Role flags are derived deterministically and consistently from AAD group claims (Priority: P3)

A user signs in and their AAD `groups` claim is evaluated to set `isAdmin`, `isEmployee`, and `isStudent`.

**Why this priority**: This mapping already works correctly today; this story exists to lock in that correctness as a regression-tested requirement now that Story 1 consolidates the downgrade logic into a single shared path, rather than to fix a defect. It's ranked lowest because nothing here is currently broken.

**Independent Test**: Sign in with fixtures covering each configured AAD group GUID (and combinations/absence thereof) and confirm the resulting role flags match the expected mapping in every case.

**Acceptance Scenarios**:

1. **Given** a user whose `groups` claim contains the configured admin group GUID, **When** the session is resolved, **Then** `isAdmin=true`.
2. **Given** a user whose `groups` claim contains none of the configured group GUIDs, **When** the session is resolved, **Then** all role flags derived from group membership are `false`.
3. **Given** a user whose `groups` claim contains multiple configured group GUIDs, **When** the session is resolved, **Then** every corresponding flag is set `true` (mappings are independent, not mutually exclusive).

### Edge Cases

- What happens when `impersonateAsStudent` is somehow set on a session that also carries a legitimately-derived `isStudent=true`? (Downgrade logic must be idempotent — re-applying it must not change the outcome.)
- How does the system behave if the shared downgrade function itself throws (e.g., malformed session object)? (Must fail closed — treat as unauthenticated/most-restrictive, never fail open to elevated access.)
- What happens if `getCurrentUser()` is called mid-token-refresh, before the refresh either succeeds or fails?
- How does a background/non-request-scoped job (no HTTP session available) distinguish "no session because nothing is signed in" from "no session because this code path never has one" when consuming the new structured error from Story 2?
- What happens if a token-refresh failure occurs while a request is already in flight (e.g., mid-stream chat response) rather than at session-check time?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST resolve the current session-scoped `UserModel` via a single shared function whenever a request needs the current user.
- **FR-002**: The system MUST implement the impersonation role-downgrade (forcing all elevated role flags to `false` and `isStudent` to `true` when `impersonateAsStudent === true`) in exactly one shared implementation.
- **FR-003**: The JWT callback, the session callback, and `userSession()`/`getCurrentUser()` MUST all invoke the single shared downgrade implementation from FR-002 rather than independently re-implementing the downgrade logic.
- **FR-004**: The system MUST derive `isAdmin`, `isEmployee`, and `isStudent` role flags from AAD `groups` claims via a single shared mapping path, consistent across all session-resolution call sites.
- **FR-005**: WHEN `getCurrentUser()` (or the session-resolution helper it wraps) is called with no active session, THE SYSTEM MUST return a structured error response consistent with the app-wide `ServerActionResponse` envelope, rather than throwing an untyped/generic `Error`.
- **FR-006**: The structured error from FR-005 MUST allow callers to distinguish "no session" from other failure classes.
- **FR-007**: WHEN AAD token refresh fails for an active session, THE SYSTEM MUST surface an explicit, user-visible re-authentication prompt on the next client session check, rather than leaving the session usable-looking with only an internal error flag set.
- **FR-008**: Re-authentication after a token-refresh failure MUST produce a fresh session with role flags re-derived via the FR-004 mapping (not carried over from the stale session).
- **FR-009**: The impersonation downgrade (FR-002/FR-003) MUST fail closed: if the shared downgrade check cannot be evaluated, the resolved user MUST be treated as unauthenticated/most-restrictive rather than granted elevated flags.

### Key Entities *(include if feature involves data)*

- **UserModel**: session-derived (not a DB row) — `name`, `email`, `image`, `token`; role flags `isAdmin`, `isEmployee`, `isContractor` (currently always `false` — see Assumptions), `isStudent`; `advancedModelAccess`; `impersonateAsStudent?` (drives the downgrade covered by Stories 1 and 4 of this spec — the mechanism by which impersonation is *initiated* is out of scope here).
- **Role-derivation mapping**: the configured AAD group-GUID → role-flag table consumed by FR-004; a single source of truth rather than logic duplicated per call site.
- **Session-resolution error response**: the structured `ServerActionResponse`-shaped result returned for "no session" and related failure modes (FR-005/FR-006), replacing the current raw thrown `Error`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the JWT-callback, session-callback, and `userSession()` code paths produce identical role-flag output for the same underlying session state, verified by a shared test suite exercised against all three entry points.
- **SC-002**: A single, shared implementation of the impersonation downgrade exists in the codebase (0 duplicate re-implementations), verified by code inspection/architecture test.
- **SC-003**: 100% of no-session `getCurrentUser()` calls in the test suite return the structured `ServerActionResponse`-shaped error rather than an unhandled thrown exception.
- **SC-004**: 100% of simulated token-refresh failures result in a user-visible re-authentication prompt on the next session check, measured across a test corpus of refresh-failure scenarios.
- **SC-005**: 100% of AAD group-claim fixtures (each configured group GUID, no group, and multiple groups) produce the expected `isAdmin`/`isEmployee`/`isStudent` flag combination.

## Assumptions

- **`isContractor` remains an intentional, documented limitation, not a functional gap this spec fixes.** The role-derivation mechanism (FR-004) already generically maps any configured AAD group GUID to a role flag; `isContractor` is hardcoded `false` today solely because no AD group GUID has been provisioned/configured for it in any environment. Once such a group is configured, wiring `isContractor` into the same shared mapping is a configuration change, not an architecture change — no new FR is required for this spec to unblock it later.
- Canvas-LTI JWT-based session bootstrap (`/api/auth/canvas-launch`) and the admin-initiated impersonation flow (`/api/auth/impersonate`, cookie signing, Student-View entry/exit) are covered by a separate spec; this spec only covers how role/session resolution behaves *given* an already-set `impersonateAsStudent` flag or an already-established session.
- The existing `ServerActionResponse` envelope (`{status:"OK", response:T} | {status:"ERROR"|"NOT_FOUND"|"UNAUTHORIZED", errors:[{message}]}`) is reused as-is for FR-005/FR-006 rather than introducing a new error shape specific to auth.
- "Re-authentication prompt" in Story 3/FR-007 means redirecting the user into the existing sign-in flow; it does not require inventing a new UI surface.
- The `groups`-claim-to-role-flag mapping table's specific GUID values are an environment/deployment configuration concern, not part of this spec's functional requirements.
