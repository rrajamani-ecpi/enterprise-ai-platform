# Feature Specification: Feedback, Changelog & Health Probes

**Feature Branch**: `017-feedback-changelog-health-probes`

**Created**: 2026-07-20

**Status**: Draft

**Input**: Derived from SSD_Document.md §3.11 (Domain: Support Features) — reframed from "as-is" discovery findings into target requirements. Source facts: the changelog source directory has been removed from the repository entirely, yet the changelog-reading function has no existence check or try/catch around it, so `/changelog` and version-alert's "latest version" lookup would fail at runtime unless the directory is repopulated out-of-band at deploy time; health endpoints are intentionally unauthenticated (required for orchestrator probing) but leak raw dependency error strings to any caller; feedback is a pure proxy — validated, ownership-checked against the caller's own thread, forwarded to an external ECPI Feedback API, never persisted locally, with proxy failures treated as non-critical (logged only); version-alert compares the latest changelog version against the user's persisted acknowledgment within a fixed 60-day "within alert period" window, with acknowledging done optimistic-then-persisted and automatic revert on a failed DB write.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Changelog and version-alert degrade gracefully when source content is missing (Priority: P1)

A user opens `/changelog`, or the app mounts and checks for a new version to alert the user about, at a time when the changelog source content is absent from the deployed build.

**Why this priority**: Today the changelog-reading function has no existence check or try/catch around it, and the changelog source directory has been removed from the repository entirely — both `/changelog` and version-alert's "latest version" lookup would throw at runtime, not just render blank, unless someone repopulates the directory out-of-band at deploy time. This is a live crash risk affecting every user on every page load (version-alert runs on app mount), so it is the most urgent fix in this spec.

**Independent Test**: With no changelog source content present, load `/changelog` directly and separately trigger the version-alert mount-time check; confirm neither throws an unhandled error and both present a defined, non-crashing state to the user.

**Acceptance Scenarios**:

1. **Given** the changelog source is missing or unreadable, **When** a user navigates to `/changelog`, **Then** the page renders a defined empty/unavailable state instead of throwing an unhandled error.
2. **Given** the changelog source is missing or unreadable, **When** the app mounts and version-alert attempts its "latest version" lookup, **Then** the lookup returns a safe fallback (no alert shown) instead of throwing, and no other app functionality is blocked.
3. **Given** the changelog source is present and well-formed, **When** the same code paths run, **Then** behavior is unchanged from today (no regression for the normal case).

---

### User Story 2 - Health probes report degraded status without leaking internal error detail (Priority: P2)

An orchestrator (or any caller, since the endpoint is intentionally unauthenticated) polls `/health` or `/api/health` while a dependency (Cosmos DB or Azure Key Vault) is failing.

**Why this priority**: Health endpoints must stay unauthenticated so the orchestrator can probe them — that part is correct and unchanged. The bug is that raw dependency error strings (which can include connection details, hostnames, or internal exception text) are currently returned to any caller. This is a real information-disclosure gap, but it doesn't block basic liveness/readiness signaling the way the P1 crash does, so it ranks below it.

**Independent Test**: Force a dependency (e.g., Cosmos DB) into a failing state, call the readiness and liveness endpoints unauthenticated, and confirm the response clearly signals degraded/unhealthy status while containing no raw internal error text (connection strings, stack traces, provider-specific exception messages).

**Acceptance Scenarios**:

1. **Given** Cosmos DB is unreachable, **When** the readiness probe is called, **Then** it reports an unhealthy/degraded status and identifies which dependency failed (by name only, e.g. "cosmos"), with no raw error string, stack trace, or connection detail in the response body.
2. **Given** Azure Key Vault is unreachable, **When** the readiness probe is called, **Then** the same sanitized-failure behavior applies for that dependency.
3. **Given** Cosmos DB is unreachable, **When** the liveness probe is called, **Then** it reports unhealthy without checking Key Vault (liveness remains Cosmos-only) and without leaking raw error text.
4. **Given** all dependencies are healthy, **When** either probe is called, **Then** it reports a healthy status (no regression for the normal case).

---

### User Story 3 - Feedback stays a pure, ownership-checked proxy that never blocks the learning experience (Priority: P3)

A user submits feedback from within a chat thread; the external ECPI Feedback API is briefly unreachable at the moment of submission.

**Why this priority**: This behavior is already implemented correctly today — feedback is validated, checked against the caller's own thread, forwarded to ECPI, never persisted locally, and a proxy failure is logged only (never surfaced to the user, to avoid disrupting the learning experience). It is included here to lock in this correct behavior as an explicit, testable requirement so future changes to this shared support-features area don't regress it; it carries no known bug, hence the lower priority.

**Independent Test**: Submit feedback referencing a thread the caller does not own and confirm rejection before any forwarding occurs; separately, submit valid feedback while the external ECPI API is simulated as unreachable and confirm the user sees no error while the failure is recorded in logs.

**Acceptance Scenarios**:

1. **Given** an authenticated user references a thread they do not own, **When** they submit feedback, **Then** the submission is rejected before any call to the external ECPI API.
2. **Given** an authenticated user references their own thread with valid feedback content, **When** they submit it, **Then** the system forwards it to the external ECPI Feedback API and does not persist a local copy.
3. **Given** the external ECPI Feedback API is unreachable or misconfigured, **When** a valid feedback submission is forwarded, **Then** the failure is logged, the user sees no error, and the rest of the learning experience continues uninterrupted.

---

### User Story 4 - Version-alert acknowledgment respects the alert window and never leaves a false "acknowledged" state (Priority: P3)

A user is shown a version alert, dismisses/acknowledges it, and the acknowledgment write to the database fails.

**Why this priority**: Like Story 3, this behavior is already implemented correctly — a fixed 60-day "within alert period" window governs whether an alert shows at all, and acknowledgment is applied optimistically in the UI but reverted automatically if the persisted write fails, so the user is never left believing an unsaved acknowledgment succeeded. Included to lock in this correct behavior as an explicit requirement of this spec, at the same priority as Story 3 since neither has a known defect.

**Independent Test**: Set the user's last acknowledgment to just inside and just outside the 60-day window and confirm alert visibility flips accordingly; separately, simulate a failed acknowledgment write and confirm the UI reverts to the un-acknowledged state.

**Acceptance Scenarios**:

1. **Given** a newer changelog version exists and the user's last acknowledgment is more than 60 days old (or absent), **When** the app mounts, **Then** the version alert is shown.
2. **Given** a newer changelog version exists and the user acknowledged within the last 60 days, **When** the app mounts, **Then** the version alert is not shown.
3. **Given** a user acknowledges an alert, **When** the persisted write succeeds, **Then** the acknowledged state remains dismissed on subsequent mounts.
4. **Given** a user acknowledges an alert, **When** the persisted write fails, **Then** the optimistic dismissal is reverted and the alert reappears rather than silently staying dismissed with no durable record.

### Edge Cases

- What happens when the changelog source exists but is malformed/partially readable (not fully absent)? Story 1's guard must treat this the same as fully missing — a defined fallback, not a crash.
- What happens when both Cosmos DB and Key Vault are simultaneously unreachable during a readiness check? The response must identify both failing dependencies by name, still with no raw error text.
- How does version-alert behave for a brand-new user with no persisted acknowledgment at all, when a changelog version already exists? (Should show the alert, per Story 4 Scenario 1's "absent" case.)
- What happens when feedback references a thread ID that doesn't exist at all (not just one owned by another user)? Must be rejected the same as an unowned thread, before any external call.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The changelog-reading function MUST check for the existence/readability of its source content and MUST NOT throw an unhandled error when that source is missing or malformed.
- **FR-002**: WHEN the changelog source is unavailable, THE `/changelog` page MUST render a defined empty/unavailable state rather than crashing or leaving the page in an indeterminate state.
- **FR-003**: WHEN the changelog source is unavailable, THE version-alert "latest version" lookup MUST return a safe fallback (resulting in no alert shown) rather than throwing, and MUST NOT block any other app functionality on app mount.
- **FR-004**: Health check endpoints (readiness and liveness) MUST remain unauthenticated to support orchestrator probing.
- **FR-005**: WHEN a dependency check fails, THE health endpoint response MUST identify the failing dependency by name and report a non-healthy status, and MUST NOT include raw exception messages, stack traces, or connection-level detail from the underlying dependency client.
- **FR-006**: The readiness probe MUST check both Cosmos DB and Azure Key Vault, each under a 5-second timeout, evaluated in parallel.
- **FR-007**: The liveness probe MUST check Cosmos DB only.
- **FR-008**: Feedback submission MUST require an authenticated session and MUST verify the referenced thread belongs to the caller before any data is forwarded externally.
- **FR-009**: Feedback MUST be forwarded to the external ECPI Feedback API and MUST NOT be persisted in this application's own data store.
- **FR-010**: WHEN the external ECPI Feedback API call fails (unreachable or misconfigured), THE system MUST log the failure and MUST NOT surface an error to the user.
- **FR-011**: Version-alert visibility MUST be determined by comparing the latest changelog version against the user's persisted acknowledgment, gated by a fixed 60-day "within alert period" window.
- **FR-012**: Acknowledging a version alert MUST update the UI state optimistically and then persist the acknowledgment; WHEN the persisted write fails, THE system MUST automatically revert the UI to the un-acknowledged state.
- **FR-013**: The main menu MUST continue to present a "View as Student" toggle to admin users, driving the existing Student-View impersonation flow.

### Key Entities *(include if feature involves data)*

- **ChangelogEntry**: a versioned changelog record (version identifier, content) sourced from application content; "latest version" is derived from this source for both the `/changelog` page and version-alert comparison.
- **FeedbackSubmission**: caller identity, referenced `threadId`, feedback content — validated and ownership-checked in-app, never persisted, forwarded as an external API payload only.
- **VersionAcknowledgment**: per-user persisted record of the last changelog version acknowledged and when, compared against the 60-day alert window on each app mount.
- **HealthCheckResult**: per-dependency status (healthy/unhealthy), dependency name, and timeout outcome — deliberately excludes raw error detail from the response surface.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of `/changelog` page loads and version-alert lookups complete without an unhandled runtime error when the changelog source is absent, across a repeated test run.
- **SC-002**: 0 raw dependency error strings, stack traces, or connection details appear in health endpoint responses across a test corpus of simulated Cosmos DB and Key Vault failures.
- **SC-003**: 100% of feedback submissions referencing a thread the caller does not own (or that doesn't exist) are rejected before any external API call, across a test corpus of ownership-violation cases.
- **SC-004**: 100% of simulated ECPI Feedback API outages result in a logged failure and zero user-visible errors, across a repeated test run.
- **SC-005**: 100% of simulated version-acknowledgment write failures result in the alert reappearing on the next mount (no silently-lost acknowledgment state), across a repeated test run.

## Assumptions

- "Changelog source content" refers to whatever content store (file-based or otherwise) backs the changelog reader at deploy time; this spec requires the reader to tolerate its absence, not that any particular content be restored to the repository.
- The specific sanitized error taxonomy for health responses (e.g., a fixed enum of dependency names) is left to implementation; the binding requirement is the absence of raw internal error text, not a prescribed response schema.
- Feedback's external ECPI API contract (request/response shape, auth mechanism) is unchanged by this spec — only the in-app validation, ownership-check, and non-persistence behavior are in scope.
- The 60-day alert-period window and optimistic-acknowledgment-with-revert mechanism are retained as-is; this spec locks in that behavior rather than changing the window length or persistence strategy.
