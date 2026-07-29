# Contract: Route Table

**Feature**: 004-chat-message-pipeline

All routes are **protected** (spec 002's fallback policy) — not on the public allow-list.

| Route | Purpose | Requirement |
|---|---|---|
| `POST /api/chat/threads` | Create a new `v3` thread for the caller | FR-002 (implicit — only `v3` threads are ever created) |
| `POST /api/chat/threads/{id}/messages` | Send a message; streams the assistant response as `text/event-stream`; persists both messages after the stream completes | FR-001–FR-006, FR-020, FR-021, FR-024 |

## Response shapes

- **Preflight rejection** (before any stream starts): a non-streamed JSON `ServerActionResponse`-shaped body — `409 THREAD_READ_ONLY`, `400 MESSAGE_TOO_LONG`, or `402 DAILY_MESSAGE_LIMIT_EXCEEDED` (with `resetsAt`). No `Content-Type: text/event-stream` response has started, so the client can distinguish "rejected" from "streaming" purely by status code before reading the body.
- **Content Safety block**: same shape as a preflight rejection — `400 CONTENT_BLOCKED` — raised before the model call, before any stream starts.
- **Streaming success**: `200`, `Content-Type: text/event-stream`, a sequence of `data: {chunk}\n\n` events, terminated by `data: [DONE]\n\n`.
- **Unhandled exception**: generic `500` with no internal detail (FR-024) — enforced by a top-level exception boundary around the endpoint, not per-call-site try/catch.

## Validation

- Every rejection path is covered by an integration test asserting: no `ChatThreadModel`/`ChatMessageModel` document was created or modified (SC-001), and the correct status/error code.
- The fail-open path (FR-005) is covered by simulating a message-limit config/counter read failure and asserting the message is allowed through (SC-002).
- The model-substitution path (FR-020/021) is covered by requesting a model outside the caller's resolved allow-list and asserting the fallback model is used, never the requested one (SC-007).
