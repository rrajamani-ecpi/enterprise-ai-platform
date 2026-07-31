# Contract: Route Table

**Feature**: 006-multi-chat-session-persistence

All routes are **protected** (spec 002's fallback policy) — not on the public allow-list.

| Route | Purpose | Requirement |
|---|---|---|
| `GET /api/multichat/session` | Get-or-create the caller's session (default: 2 empty quadrants) — restores on load (FR-002) | FR-001, FR-002 |
| `POST /api/multichat/session/quadrants` | Add a quadrant; rejected at the 4-quadrant cap | FR-005 |
| `DELETE /api/multichat/session/quadrants` | Remove the highest-position quadrant; at the 2-quadrant floor, clears its assignment instead (FR-004) | FR-004 |
| `PUT /api/multichat/session/quadrants/{position}/model` | Assign a model to a quadrant; validated against spec 014's catalog | FR-001 (persistence), spec 014 (validation) |
| `POST /api/multichat/session/messages` | Parallel send: one `text`, dispatched to every quadrant with an assigned model; streams a tagged `text/event-stream` (one `QuadrantEvent` per event) | FR-003, FR-006, FR-010, FR-011 |

## Response shapes

- **Session GET/mutations**: `ServerActionResponse`-shaped JSON reflecting the current `MultiChatSession` state.
- **Quadrant cap rejection**: `400 QUADRANT_CAP_EXCEEDED`.
- **Parallel-send stream**: `200`, `Content-Type: text/event-stream`, events shaped `data: {"position":0,"kind":"chunk","content":"..."}\n\n`, one `{"kind":"done"}` per quadrant as it finishes, `{"kind":"error","content":"..."}` for a quadrant whose on-demand thread creation or send fails — never a generic 500 for the whole request over one quadrant's failure (FR-006).

## Validation

- Every add/remove is covered by a test asserting the resulting count stays in `[2, 4]` and that removal-at-the-floor clears rather than deletes (SC-001 adjacent).
- The restore-after-refresh scenario is covered by an integration test: mutate a session, `GET` again, assert identical quadrant count/assignments/thread associations (SC-001).
- The parallel-send endpoint is covered by a test with one quadrant configured to fail (fake thread store throws) and others to succeed, asserting the failing quadrant's `error` event doesn't block the others' `chunk`/`done` events (SC-002, SC-003, SC-005).
