# Quickstart: Validating Multi-Chat Session Persistence (R1 subset)

**Feature**: 006-multi-chat-session-persistence | **Date**: 2026-07-29

Validation guide for the R1-scoped subset (US1, US2, US4). US3 (Chat-Home) is R2 — not exercised here.

## Prerequisites

- Specs 002, 004, and 014's walking skeleton running (session/role, chat pipeline, model access).
- An Azure Cosmos DB account (or emulator) — reuses spec 004's `chat` container and `Cosmos:AccountEndpoint` config. **Not required for the automated test suite** — tests substitute an in-memory `IMultiChatSessionStore`/`IChatThreadStore`, matching spec 004's testing pattern.

## Setup

```bash
dotnet restore
dotnet build EnterpriseAIPlatform.sln
dotnet run --project src/EnterpriseAIPlatform.Web
```

## Validation scenarios (one per in-scope story/requirement)

| Story/FR | Steps | Expected (pass) |
|---|---|---|
| **US1 · Restore on load** (FR-001/002) | `GET /api/multichat/session` (creates a fresh 2-quadrant session); assign a model to quadrant 0; `GET` again. | Same quadrant count and model assignment returned both times (SC-001). |
| **US1 · Add/remove bounds** (FR-004/005) | `POST .../quadrants` repeatedly past 4; `DELETE .../quadrants` repeatedly past 2. | Adding past 4 is rejected (count stays 4); removing past 2 clears the last quadrant's assignment instead of dropping below 2. |
| **US1 · On-demand thread persists immediately** (FR-003) | Assign a model to an empty quadrant; send a message via the parallel-send endpoint; immediately `GET /api/multichat/session` again (simulating a refresh mid-stream). | The quadrant's `ThreadId` is already set on the second `GET`, before the stream even completes. |
| **US2 · Failed send surfaces an error, doesn't fault the request** (FR-006) | Configure one quadrant's underlying thread-creation to fail (test-only fake); send via the parallel-send endpoint with 2+ quadrants assigned. | The response is `200`/`text/event-stream` (not 500); the failing quadrant's stream contains an `error` event; other quadrants' `chunk`/`done` events still arrive (SC-002/003). |
| **US4 · Parallel dispatch, side-by-side** (FR-010/011) | Assign different models to 2–4 quadrants; send one message via the parallel-send endpoint. | All quadrants' `chunk` events appear in the same response, interleaved as they arrive — no quadrant waits for another to finish (SC-005). |

**Deferred to R2** (not exercised by this quickstart): US3 (Chat-Home starred personas) and any persona-assignment behavior (`PersonaId` stays null throughout).

## Automated test commands

```bash
dotnet test tests/EnterpriseAIPlatform.UnitTests          # quadrant floor/cap, on-demand creation persistence, fan-in merge
dotnet test tests/EnterpriseAIPlatform.IntegrationTests   # session restore, parallel-send endpoint (fakes, no live Cosmos)
dotnet test tests/EnterpriseAIPlatform.ArchitectureTests  # single IMultiChatSessionStore implementation
```

## Done when

- [ ] All five R1-scoped scenarios above pass.
- [ ] All three test projects green, covering SC-001, SC-002, SC-003, SC-005.
- [ ] Architecture test confirms exactly one `IMultiChatSessionStore` implementation (Principle IV).
- [ ] SC-004 (US3) is explicitly recorded as deferred to R2, not silently skipped.
