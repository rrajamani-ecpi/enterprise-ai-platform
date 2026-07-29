# Contract: Route Table

**Feature**: 014-model-access-config-management

All routes below are **protected** (spec 002's fallback policy applies); none are added to the public allow-list. R1 ships these as API routes only — no admin UI consumes the mutation routes yet (deferred to R2).

## Read (any authenticated caller — FR-004/005)

| Route | Purpose | Requirement |
|---|---|---|
| `GET /api/model-access/available-models` | Caller's effective model list (D3 computation) | FR-003 |
| `GET /api/config/message-limit` | Current message-limit config (defaults if unset — Edge Cases) | FR-004 |
| `GET /api/config/persona-generation-model` | Current persona-generation allow-list config | FR-005 |
| `GET /api/model-catalog` | Full catalog metadata for enabled, non-deleted models (FR-011) | FR-011 |

## Write (`RequireAdmin` — FR-001/006/007)

| Route | Purpose | Requirement |
|---|---|---|
| `PUT /api/admin/system-config` | Update `SystemModelConfig` (role allow-lists, embedding/fallback model) | FR-001 |
| `PUT /api/admin/model-config/{id}` | Create/update a `ModelConfigDocument` | FR-001, FR-011 |
| `DELETE /api/admin/model-config/{id}` | Soft-delete only — sets `IsDeleted=true` | FR-002 |
| `PUT /api/admin/config/message-limit` | Update message-limit caps (server-revalidated ≥1 integer) | FR-006, FR-008 |
| `PUT /api/admin/config/persona-generation-model` | Update the persona-generation allow-list | FR-007, FR-009 |

## User preferences (FR-010 — fix, not new surface)

| Route | Purpose |
|---|---|
| `/api/user/preferences/*` (all existing sub-routes) | Each now performs its own explicit auth check → 401 `UNAUTHORIZED` for no session, instead of relying on an internal helper's uncaught throw (→ 500) |

## Validation

- Every write route is covered by a `WebApplicationFactory` test asserting non-admin rejection with no document change (SC-001) and admin success (SC-001).
- The read/write route pairs for message-limit and persona-generation-model are covered by a single test asserting the asymmetry directly (SC-004).
- `/api/user/preferences/*` is covered by a parametrized test over every sub-route asserting 401, never 500, for no session (SC-006).
