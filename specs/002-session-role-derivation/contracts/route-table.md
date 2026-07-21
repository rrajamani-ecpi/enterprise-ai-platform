# Contract: Route Table (public vs protected)

**Feature**: 002-session-role-derivation

The explicit, closed-by-default route classification enforced by the fallback authorization policy (FR-011). Anything **not** listed as public requires an authenticated session; admin routes additionally require `RequireAdmin` (FR-012).

## Public routes (no session required — the entire allow-list)

| Route | Purpose |
|---|---|
| `GET /health` (and readiness/liveness probes) | Health check (owned in detail by spec 017) |
| `/signin` / OIDC callback endpoints | Entra sign-in flow (FR-010) |
| `/lti/launch` | LMS/Canvas launch entry (owned by spec 003) |
| `/lti/error` | LMS launch error surface (spec 003) |

> Adding a route here is a deliberate, reviewed act — the fallback policy denies everything else.

## Protected routes (authenticated session required)

All application routes not listed above. Unauthenticated requests are redirected to sign-in (FR-011, SC-006).

## Admin-only routes/actions (`RequireAdmin`)

Any route or server action flagged admin-only (e.g., admin configuration surfaces owned by later specs). Rejected server-side for non-admin sessions regardless of UI state (FR-012/013, SC-007).

## Validation

- The public allow-list is bound via Options and covered by a `WebApplicationFactory` route-coverage test asserting: every public route is reachable unauthenticated, and a representative protected + admin route set is denied appropriately (SC-006/007).
