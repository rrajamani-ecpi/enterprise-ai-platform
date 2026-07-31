# Contract: Route Table

**Feature**: 017-feedback-changelog-health-probes

## Public (spec 002 allow-list — unchanged routes, upgraded implementation)

| Route | Purpose | Requirement |
|---|---|---|
| `GET /health/live` | Liveness — Cosmos DB only | FR-004, FR-007 |
| `GET /health/ready` | Readiness — Cosmos DB + Key Vault, parallel, 5s timeout each | FR-004, FR-006 |

Both remain on spec 002's public allow-list (`PublicRoutes:Paths`) — no change to that configuration, only to what the endpoints actually check.

## Protected (spec 002's fallback policy)

| Route | Purpose | Requirement |
|---|---|---|
| `GET /api/changelog` | All changelog entries, newest first (empty list if source missing) | FR-001, FR-002 |
| `GET /api/changelog/acknowledgment` | The caller's persisted acknowledgment (`null` if none yet) + whether the alert should currently show | FR-011 |
| `POST /api/changelog/acknowledgment` | Persist an acknowledgment of the current latest version | FR-012 |
| `POST /api/feedback` | Submit feedback for a thread the caller owns; forwarded to ECPI, never persisted, never surfaces a forwarding failure to the caller | FR-008–010 |

## Response shapes

- **`GET /health/live` / `GET /health/ready`**: `200` (Healthy) or `503` (Unhealthy/Degraded — ASP.NET Core health checks' default status-code mapping), body `{ "status": "Healthy" | "Degraded" | "Unhealthy", "checks": [{ "name": "cosmos", "status": "Healthy" }, ...] }` — never `Exception`/`Description` (FR-005).
- **`GET /api/changelog`**: `200`, `[]` if the source is missing/malformed (FR-002) — never a 500.
- **`POST /api/changelog/acknowledgment`**: `200` on successful persistence; a non-2xx on failure (D4 — the client, not built in this pass, is expected to revert its optimistic UI on anything but 2xx).
- **`POST /api/feedback`**: `403`/`404` if the referenced thread isn't the caller's own (FR-008, before any external call); otherwise always `200` to the caller, regardless of whether the downstream ECPI call actually succeeded (FR-010 — logged server-side only).

## Validation

- `/health/live`/`/health/ready` are covered by tests for both the unconfigured-Healthy case (matches every existing test factory) and a configured-but-unreachable-Unhealthy case (via a fake failing `IHealthCheck`), asserting the response never contains exception/connection text (SC-002).
- `/api/feedback` is covered by a test submitting against a thread the caller doesn't own (rejected before any forwarder call) and a thread the caller does own with the forwarder simulated as failing (caller still sees success, failure only logged) (SC-003/004).
- `/api/changelog/acknowledgment` is covered by a round-trip test (post, then get) and a window-boundary test (just inside vs. just outside 60 days) (SC-005).
