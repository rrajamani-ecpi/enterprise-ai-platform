# Contract: Authorization Policy Catalog

**Feature**: 002-session-role-derivation

The single server-side gate (FR-011/012/013, Principle II). Blazor `<AuthorizeView>`/`[Authorize]` may mirror these for UX, but the **policy is the boundary** — UI state is advisory only.

## Policies

| Policy name | Requirement | Applies to | Requirement source |
|---|---|---|---|
| *(fallback policy)* | Authenticated user required | **Every** endpoint/route not on the public allow-list | FR-011 |
| `RequireAdmin` | `IsAdmin == true` (server-derived) | Admin-only routes and server actions | FR-012 |
| `RequireAuthenticated` | Authenticated user | Explicit opt-in for endpoints that want it named | FR-011 |

**Rules**:
- The fallback policy denies by default; a route becomes public **only** by being on the explicit allow-list (see route-table.md). This prevents accidental widening of the public set (FR-011 edge case).
- `RequireAdmin` evaluates the server-derived `IsAdmin` flag from the transformed principal (D2), never a client-supplied value (FR-013).
- Model-access / tool-access / sharing / delete-write decisions resolve server-side via these (or feature-specific) policies; client UI gating is never the basis for the decision (FR-013).
- Downgrade under impersonation is already applied upstream in the claims transformation, so `RequireAdmin` correctly denies an impersonating admin (Story 1).

## Failure behavior

| Condition | Result |
|---|---|
| Unauthenticated request to protected route | Challenge → redirect to Entra sign-in (FR-011) |
| Non-admin request to `RequireAdmin` route/action (even if UI exposed it) | 403 / server-side rejection (FR-012/013, SC-007) |
| Token refresh failed | Re-auth challenge on next check (FR-007) |
