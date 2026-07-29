# Contract: Authorization Policy Reuse

**Feature**: 014-model-access-config-management

This feature introduces **no new authorization policies** — it reuses spec 002's catalog verbatim (Principle IV: one implementation per concern, extended to "one policy catalog").

## Policy usage in this feature

| Policy name (from spec 002) | Applies to here | Requirement source |
|---|---|---|
| *(fallback policy — authenticated user required)* | Message-limit read, persona-generation-model read, `/api/user/preferences/*` | FR-004/005/010 |
| `RequireAdmin` | System-config, model-config, message-limit write, persona-generation-model write | FR-001, FR-006, FR-007 |

**Rules**:
- No route in this feature is added to spec 002's public allow-list — every route here requires at minimum an authenticated session.
- The read/write asymmetry (D5) is expressed entirely through *which policy each endpoint declares*, not through in-handler branching — `RequireAdmin` on the write endpoint, the fallback policy (authenticated-only) on the read endpoint, same underlying service.
- `RequireAdmin` evaluates spec 002's server-derived `IsAdmin` flag; this feature never re-derives or duplicates that check.

## Failure behavior

| Condition | Result |
|---|---|
| Non-admin calls a mutation endpoint (system-config, model-config, message-limit write, persona-gen-model write) | Rejected before any write occurs (FR-001, SC-001) |
| Unauthenticated call to a read endpoint (message-limit, persona-gen-model) | Redirect/401 per spec 002's fallback policy |
| Unauthenticated call to `/api/user/preferences/*` | Explicit 401 with the `ServerActionResponse` `UNAUTHORIZED` shape (FR-010, SC-006) — not 500 |
