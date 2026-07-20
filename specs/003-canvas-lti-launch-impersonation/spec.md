# Feature Specification: Canvas LTI Launch & Student-View Impersonation Hardening

**Feature Branch**: `003-canvas-lti-launch-impersonation`

**Created**: 2026-07-20

**Status**: Draft

**Input**: Derived from SSD_Document.md §3.1 (Auth & Identity — Canvas LTI launch and admin Student-View impersonation) and §5 (Architectural Debt — JTI-replay-cache multi-replica gap) — reframed from "as-is" discovery findings into target requirements. Source facts: `/api/auth/canvas-launch` verifies a signed JWT from an external Canvas Integration Service (RS256/JWKS, pinned issuer/audience, required claims, one-time `jti` replay protection) and mints a NextAuth session directly (`isStudent:true`, 8-hour cookie, redirect to `/lesson/{personaId}` after regex validation); the in-memory `jti` replay cache does not share state across replicas even though Redis is required in production, and nothing enforces that at runtime; admin impersonation mints a 1-hour HMAC cookie bound to the admin's own hashed identity, constant-time-compared, `__Host-` prefixed in production, and re-verified on every `jwt()` callback as the sole source of truth for the role downgrade, but the audit event today is console-log only. The general session-resolution/role-derivation logic and the three-way duplication of the downgrade check are out of scope here (see spec 002).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Canvas student is launched directly into their lesson via a verified, single-use token (Priority: P1)

A student clicks a Canvas assignment link; the external Canvas Integration Service validates the real LTI 1.3 launch and hands this app a signed JWT, which this app must verify before minting a session and landing the student on their lesson.

**Why this priority**: This is the sole entry point for every Canvas-launched student — nothing else in this spec matters if a valid token can't reliably produce a working session, or if an invalid one can.

**Independent Test**: Present a valid signed launch JWT (correct RS256/JWKS signature, pinned issuer/audience, unexpired, unused `jti`, all required claims present) and confirm a session is minted with `isStudent:true`, all other role flags `false`, an 8-hour cookie, and a redirect to `/lesson/{personaId}`. Separately, present tokens with a bad signature, wrong issuer/audience, expired `exp`, missing claims, or an invalid `personaId` and confirm each is rejected.

**Acceptance Scenarios**:

1. **Given** a valid, unused launch JWT, **When** it is posted to `/api/auth/canvas-launch`, **Then** the system mints a session (`isStudent:true`, other role flags `false`, 8-hour expiry) and redirects to `/lesson/{personaId}`.
2. **Given** a JWT with a bad signature, wrong `iss`/`aud`, expired `exp`, or a missing required claim, **When** it is posted, **Then** the system rejects it with a 303 redirect to `/lti/error?code=<SPECIFIC_CODE>` and mints no session.
3. **Given** a valid JWT whose embedded `personaId` does not match `^[A-Za-z0-9_-]{1,128}$`, **When** it is posted, **Then** the system rejects the redirect rather than interpolating the raw value into the response.
4. **Given** `NEXTAUTH_SECRET` is unset at launch time, **When** an otherwise-valid JWT is posted, **Then** the system redirects to `/lti/error?code=SESSION_CREATE_FAILED` rather than crashing.

---

### User Story 2 - Replayed launch tokens are rejected in every deployment topology, including multi-instance production (Priority: P1)

An attacker (or an accidental double-submit) replays a previously-used launch JWT. Today's replay guard is an in-memory `jti` cache that does not share state across replicas, so a token consumed on one instance is treated as unused on another — silently defeating one-time-use protection in exactly the topology (multi-instance production) it's supposed to protect.

**Why this priority**: This is a live security gap, not a documentation gap — a replayed token can currently mint a second session in production. It's ranked alongside Story 1 because a launch flow that verifies tokens correctly but can still be replayed is not meaningfully more secure than no replay check at all.

**Independent Test**: Start the app in a multi-instance-capable configuration without a shared cache backend configured and confirm it fails to start with an explicit error. Separately, with a shared cache backend configured, replay the same `jti` against two different instances and confirm the second attempt is rejected.

**Acceptance Scenarios**:

1. **Given** a deployment configured as multi-instance-capable with no shared cache backend (e.g., `REDIS_URL`/`CACHE_PROVIDER`) set, **When** the app starts, **Then** startup fails fast with an explicit, actionable error instead of booting silently.
2. **Given** a shared cache backend is configured, **When** the same `jti` is consumed on one instance and then replayed against a different instance, **Then** the second attempt is rejected as a replay.
3. **Given** a single-instance/local-dev configuration with no shared cache backend, **When** the app starts, **Then** it starts normally using an in-memory cache (no regression for local development).

---

### User Story 3 - Admin Student-View impersonation cannot outlive or exceed its bound scope (Priority: P2)

An admin enters Student View to see the app as a student would; every subsequent request must re-derive the downgraded role from a cookie that only that admin could have produced, and that cookie must not be usable by or attributable to anyone else.

**Why this priority**: This behavior is largely already correct (1-hour expiry, own-identity binding, constant-time comparison, `__Host-` prefix, re-verification on every `jwt()` callback) — it's ranked below Stories 1–2 because it protects an internal admin tool rather than the primary learner entry point, but a forgeable or overlong-lived impersonation cookie is still a meaningful privilege-boundary risk.

**Independent Test**: Start impersonation as Admin A, confirm the resulting cookie is bound to Admin A's own hashed identity and expires at 1 hour; attempt to present Admin A's cookie value tampered or under Admin B's session and confirm rejection; confirm the `__Host-` prefix is present in a production-configured environment.

**Acceptance Scenarios**:

1. **Given** Admin A starts Student View, **When** the impersonation cookie is issued, **Then** it is HMAC-signed, bound to Admin A's own hashed identity, and expires in 1 hour.
2. **Given** Admin A's impersonation cookie, **When** it is tampered with or replayed under a different admin's session, **Then** signature/binding verification fails and the downgrade does not apply.
3. **Given** a production-configured deployment, **When** the impersonation cookie is set, **Then** it uses the `__Host-` prefix.
4. **Given** an active impersonation cookie, **When** any subsequent `jwt()` callback runs, **Then** it independently re-verifies the cookie as the sole source of truth for the role downgrade rather than trusting a cached decision.

---

### User Story 4 - Every impersonation session start is durably recorded for audit (Priority: P3)

Compliance and security review need to know, after the fact, which admin viewed the app as a student, when, and for how long — today this event is written only to console output, which is not queryable and does not survive log rotation or process restart.

**Why this priority**: Important for compliance and incident response, but it doesn't block the impersonation flow itself from working correctly, so it's ranked below Stories 1–3.

**Independent Test**: Start an impersonation session, then confirm a durable, queryable audit record exists (admin identity, timestamp, scope) independent of application console/log output, and that it survives an application restart.

**Acceptance Scenarios**:

1. **Given** an admin starts Student View, **When** the impersonation cookie is issued, **Then** a durable audit record is written capturing the admin's identity, a timestamp, and the impersonation scope.
2. **Given** a durable audit record was written, **When** the application process restarts, **Then** the record remains retrievable (i.e., it is not solely in-memory or console-only).

### Edge Cases

- What happens when the `jti` cache backend itself is unreachable at verification time (not merely unconfigured)? Replay protection is a security control, so a lookup failure should not silently be treated as "not yet used."
- What happens if an admin's own session expires or is revoked while an impersonation cookie issued under it is still within its 1-hour window?
- How does the system behave if two Canvas launches for the same student/course arrive concurrently with different `jti`s (both otherwise valid)?
- What happens when a multi-instance deployment's shared-cache backend is configured but transiently unreachable at startup — does the fail-fast check distinguish "unset" from "unreachable"?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST verify the Canvas launch JWT's RS256 signature against the published JWKS, and reject it if the signature does not validate.
- **FR-002**: The system MUST pin and check the JWT `iss` (`canvas-integration-service`) and `aud` (`accelerator-app`) claims, rejecting any mismatch.
- **FR-003**: The system MUST validate `exp`/`nbf`/`iat` and reject expired, not-yet-valid, or malformed tokens.
- **FR-004**: The system MUST reject a launch JWT missing any required claim.
- **FR-005**: The system MUST atomically consume the JWT's `jti` as one-time-use and reject any launch presenting a previously-consumed `jti`.
- **FR-006**: On successful verification, the system MUST mint a NextAuth session with `isStudent:true`, all other role flags `false`, and an 8-hour cookie expiry.
- **FR-007**: The system MUST validate the JWT's `personaId` against `^[A-Za-z0-9_-]{1,128}$` before using it in the post-launch redirect to `/lesson/{personaId}`.
- **FR-008**: WHEN `NEXTAUTH_SECRET` is unset at launch time, THE SYSTEM SHALL redirect to `/lti/error?code=SESSION_CREATE_FAILED` rather than crashing.
- **FR-009**: The `jti` replay-protection cache MUST use a shared backend (e.g., Redis) in any deployment configured as multi-instance-capable.
- **FR-010**: The system MUST fail fast at startup with an explicit error when configured for multi-instance-capable deployment without a shared cache backend configured — this MUST NOT be left as a documentation-only expectation.
- **FR-011**: A single-instance/local-dev configuration MUST be permitted to run with an in-memory `jti` cache without triggering the startup failure in FR-010.
- **FR-012**: The impersonation endpoint MUST mint a 1-hour HMAC-signed cookie bound to the initiating admin's own hashed identity.
- **FR-013**: Impersonation cookie verification MUST use a constant-time comparison.
- **FR-014**: The impersonation cookie MUST use the `__Host-` prefix in production.
- **FR-015**: Every `jwt()` callback invocation MUST independently re-verify the impersonation cookie as the sole source of truth for the student-role downgrade (the single-enforcement-point consolidation of this check across call sites is addressed in spec 002, not here).
- **FR-016**: Every impersonation session start MUST be recorded in a durable, queryable audit log capturing the admin's identity, a timestamp, and the impersonation scope — console-only logging MUST NOT be the sole record.

### Key Entities *(include if feature involves data)*

- **CanvasLaunchJWT**: external, RS256-signed contract — `iss`, `aud`, `exp`/`nbf`/`iat`/`jti`, `canvas_env`, `canvas_user_id`, `canvas_course_id`, `canvas_assignment_id?`; single-use per `jti`.
- **ImpersonateCookiePayload**: `{userIdHash, mode:"student", exp}` — HMAC-SHA256-signed, bound to the issuing admin's own hashed identity, 1-hour lifetime.
- **JTI Replay Cache Entry**: a consumed-token marker keyed by `jti`, requiring shared (cross-instance) visibility wherever the app runs as more than one instance.
- **Impersonation Audit Record**: durable record of an impersonation session start — admin identity, timestamp, scope — independent of application log output.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of valid Canvas launch JWTs (correct signature/issuer/audience/claims/expiry, unused `jti`) result in a minted session and correct redirect; 0% of tokens with any single invalid attribute succeed.
- **SC-002**: 0 replay successes in a multi-instance test topology once a shared cache backend is configured; 100% of multi-instance-configured deployments without a shared cache backend fail to start.
- **SC-003**: 0 impersonation cookies accepted across a forged/cross-admin/tampered-cookie test suite.
- **SC-004**: 100% of impersonation session starts produce a durable audit record that remains retrievable after an application restart.

## Assumptions

- The three-way duplication of the role-downgrade check (`jwt()`, `session()`, `userSession()`) is not resolved by this spec; FR-015 requires re-verification wherever the check currently runs, while consolidating it to a single enforcement point is spec 002's concern.
- "Multi-instance-capable environment" reuses whatever deployment-topology signal already exists in configuration (e.g., an existing scaling/replica-count setting) rather than introducing a new one solely for this check.
- Redis is the reference shared-cache backend per the source documentation; any distributed store offering atomic check-and-set semantics satisfies FR-009/FR-010.
- The durable audit log in FR-016 reuses existing structured-logging/persistence infrastructure elsewhere in the codebase rather than introducing a new storage system, consistent with the logging-convergence target noted in SSD_Document.md §5.
- Open-redirect hardening on the NextAuth `redirect` callback and the absence of a root `middleware.ts` (route-group gating plus `proxy.ts` instead) are unchanged by this spec.
