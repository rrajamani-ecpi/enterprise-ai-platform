# Phase 0 Research: Session Resolution & Role Derivation

**Feature**: 002-session-role-derivation | **Date**: 2026-07-21

This document resolves the technology unknowns implied by translating the spec's legacy (Next.js/NextAuth) source facts onto the constitution's target stack (.NET 10 / Blazor / Microsoft.Identity.Web / Microsoft Entra ID). Each decision follows the format: Decision / Rationale / Alternatives considered.

---

## D1. Authentication & sign-in library

- **Decision**: Use **Microsoft.Identity.Web** for OIDC sign-in against Microsoft Entra ID, wired into the Blazor Web App host, with the group claim configured for role derivation.
- **Rationale**: First-party Microsoft library, the constitution mandates Entra ID SSO (FR-010), and it provides token acquisition/refresh and downstream-API auth out of the box (needed for FR-007/008). Integrates natively with ASP.NET Core authorization policies (FR-011/012).
- **Alternatives considered**: Raw `Microsoft.AspNetCore.Authentication.OpenIdConnect` (more wiring, no built-in token cache/refresh helpers); third-party IdentityModel (redundant with the first-party option and off the Azure-only path).

## D2. Single source of truth for role derivation + impersonation downgrade (FR-002/003/004 — Principle IV)

- **Decision**: Implement one `IClaimsTransformation` in Infrastructure that (a) maps Entra group-GUID claims to role flags via a single configured mapping, and (b) applies the `impersonateAsStudent` downgrade exactly once. All consumers read the resulting `ClaimsPrincipal` through one `ICurrentUserAccessor`; the downgrade logic itself lives in a single `RoleDowngrade` function in Application.
- **Rationale**: Replaces the legacy three-site duplication (jwt/session/userSession callbacks) with one enforcement point. `IClaimsTransformation` runs once per authentication in the ASP.NET Core pipeline, so every request/circuit sees identical flags (SC-001). Keeping the pure downgrade function in Application (framework-free) lets the architecture test assert a single implementation (SC-002).
- **Alternatives considered**: Applying the downgrade in a middleware *and* in a Blazor `AuthenticationStateProvider` (re-introduces duplication); computing roles lazily at each call site (the exact anti-pattern the spec exists to remove).

## D3. Canonical current-user accessor (FR-001)

- **Decision**: A single `ICurrentUserAccessor.GetCurrentUser()` returns a `ServerActionResponse<UserModel>`. It reads from the authenticated `ClaimsPrincipal` (post-transformation), never re-deriving roles. Backed by `IHttpContextAccessor` in request scope and by the Blazor `AuthenticationStateProvider` in circuit scope, unified behind the one interface.
- **Rationale**: One resolver for "who is the current user" (FR-001), consumed identically by request-scoped endpoints and Blazor components. Returning `ServerActionResponse<UserModel>` satisfies FR-005 uniformly.
- **Alternatives considered**: Exposing `ClaimsPrincipal` directly to feature code (leaks claim-shape details and invites per-feature role re-derivation); separate accessors for Blazor vs endpoints (duplication).

## D4. Structured result envelope (FR-005/006)

- **Decision**: Reuse the app-wide `ServerActionResponse<T>` shape from the spec's Assumptions: `{ Status: OK, Response: T } | { Status: ERROR | NOT_FOUND | UNAUTHORIZED, Errors: [{ Message }] }`, modeled as a C# record with a `ResponseStatus` enum. No-session resolution returns `UNAUTHORIZED`.
- **Rationale**: The spec explicitly reuses this envelope rather than inventing an auth-specific error; callers can branch on it like any other domain result (FR-006). Avoids raw exceptions for an expected failure mode (Principle III).
- **Alternatives considered**: Throwing a typed `SessionNotFoundException` (still an exception for an expected path); `Result<T>` from a third-party library (adds a dependency for a shape the codebase already standardizes on).

## D5. Route protection & admin gating (FR-011/012/013 — Principle II)

- **Decision**: ASP.NET Core **authorization policies** as the single server-side gate: a fallback policy requires an authenticated user for every endpoint/route except a designated **public-route allow-list** (health check, sign-in, LMS launch, LMS error); a `RequireAdmin` policy gates admin-only routes/actions. Blazor components use `<AuthorizeView>`/`[Authorize]` for UX only — the server policy is the boundary.
- **Rationale**: Centralizes the access decision server-side; the public allow-list is explicit and closed-by-default (fallback policy denies anything not listed), matching FR-011's "without accidentally widening the public set" edge case.
- **Alternatives considered**: Per-page `@attribute [Authorize]` only (easy to forget a page → open route); client-side route guards (advisory only, never a boundary per Principle II).

## D6. Token-refresh failure → forced re-authentication (FR-007/008)

- **Decision**: Use Microsoft.Identity.Web token acquisition; on `MsalUiRequiredException`/refresh failure, redirect the user into the existing Entra sign-in **challenge** (re-auth prompt) on the next session check, and rebuild the session with freshly derived role flags on success.
- **Rationale**: Surfaces the failure explicitly instead of leaving a usable-looking session with only an internal error flag (Principle III, FR-007). Re-auth re-runs the D2 claims transformation, satisfying FR-008 (no stale flags carried over).
- **Alternatives considered**: Setting a session error flag and letting downstream calls fail (the legacy behavior the spec fixes); silent background refresh only (doesn't cover refresh-token expiry/revocation).

## D7. Impersonation representation & fail-closed evaluation (FR-009)

- **Decision**: Represent impersonation as a signed claim/state on the principal; the D2 transformation treats a missing/unreadable/ malformed impersonation state as **most-restrictive** (deny elevation), and the downgrade function is idempotent.
- **Rationale**: FR-009 requires fail-closed; the spec's edge cases require idempotency and safe handling of a malformed session object. Evaluating impersonation inside the single transformation keeps it from being bypassed.
- **Alternatives considered**: Trusting a client-supplied impersonation flag (violates Principle II — client input is never a security signal); throwing on malformed state (fails loud but could fail *open* if the catch path defaults to elevated).

## D8. Identity hashing for storage partition keys (FR-014)

- **Decision**: A single `IIdentityHasher` computes `SHA-256(normalize(email))` where `normalize` lowercases and trims; the hex/base64url digest is the Cosmos partition key. Canvas-launched students use the spec-003 LMS identity-hash scheme instead (out of scope here).
- **Rationale**: Deterministic across casing/whitespace variants (SC-008); avoids raw email as a partition key (data-protection hardening, FR-014). One hasher (Principle IV) enforced at the persistence boundary (Principle V).
- **Alternatives considered**: HMAC with a secret key (adds key-management/rotation burden not required by the spec; SHA-256 of a normalized identifier is sufficient for a non-reversible partition key here); using the raw Entra `oid` (stable, but the spec/PRD standardize on hashed normalized email for the corporate identity path).

## D9. `isContractor` (Assumption, not a gap)

- **Decision**: Wire `isContractor` through the same D2 group→flag mapping; leave it `false` until an Entra group GUID is configured for it. No new code path.
- **Rationale**: The spec designates this an intentional, documented config limitation, not an architecture change. The generic mapping already accommodates it.
- **Alternatives considered**: Special-casing contractor logic (unnecessary; contradicts Principle IV).

## D10. Testing strategy (SC-001…SC-008)

- **Decision**: **xUnit** across three test projects — Unit (role mapping, downgrade idempotency/fail-closed, hash determinism), Integration via `WebApplicationFactory` (public vs protected routes, admin gating server-side even when a fake UI exposes the control, no-session response shape), and Architecture (reflection assertion that exactly one downgrade implementation and one current-user resolver exist). **bUnit** covers the Blazor auth-state re-auth prompt.
- **Rationale**: Maps one measurable success criterion to a concrete test surface; the architecture test directly enforces SC-002/Principle IV so duplication can't silently return.
- **Alternatives considered**: Integration-only (can't cheaply prove "single implementation"); manual verification (not falsifiable, violates Principle VI).

## D11. Configuration validation (Principle V)

- **Decision**: Bind the group-GUID→role-flag mapping and the public-route allow-list via the Options pattern with `ValidateOnStart`, failing app startup if the mapping is missing/malformed.
- **Rationale**: Business-rule gating (which groups grant which roles) is enforced at the schema/config layer, not per call site (Principle V), and fails loud at boot rather than silently mis-authorizing at runtime (Principle III).
- **Alternatives considered**: Reading config ad hoc at each call site (drift + no startup validation).

---

**All NEEDS CLARIFICATION resolved.** No open unknowns block Phase 1.
