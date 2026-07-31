# Implementation Plan: Multi-Chat Session Persistence

**Branch**: `006-multi-chat-session-persistence` | **Date**: 2026-07-29 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/006-multi-chat-session-persistence/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

This is **Layer 4** of [the sequencing plan](../../docs/spec-sequencing-plan.md) — depends on spec 004 (chat pipeline) and spec 014 (model access), both already implemented.

Built **as written** (per explicit user decision after flagging a mismatch with `docs/Release1-MVP Plan.md`'s shorthand "006: list/switch/rename threads," which doesn't match this spec's actual multi-quadrant, parallel-model-comparison content). **R1 scope: US1, US2, US4 (all P1)**. **Deferred: US3** (Chat-Home starred personas, P3) — no data-loss/reliability risk, and personas (specs 009/010) aren't in R1 anyway, so there's nothing to star yet.

**Key R1 scoping decision — persona vs. model assignment**: spec.md's Key Entities describe per-quadrant **persona** assignment, but personas (specs 009/010) don't exist in R1. Per-quadrant **model** assignment (US4's actual mechanism — FR-010/011 dispatch "to every quadrant's assigned model") is fully buildable now via spec 014. R1 therefore implements the full `MultiChatSession` schema (quadrant count + persona slot + model slot + thread association) so personas slot in later without a schema change, but **only the model-assignment path is functional** — `PersonaId` stays nullable/unset in R1, mirroring spec 004's precedent of shipping an always-empty `DataProducts` field ahead of spec 019.

**Key R1 design simplification — no new fan-out infrastructure needed for the "send once" UX**: US4's "author once, dispatch to N models" doesn't require a bespoke multi-model orchestration layer — it requires (a) each quadrant's thread/model association to be persisted (US1) and (b) a way to send the same text to N quadrants' independent chat pipelines without one blocking another. R1 implements this as **one new endpoint** that fans a single message out to every assigned quadrant concurrently via spec 004's already-existing `IChatPipeline`, merging each quadrant's independent stream into one tagged SSE response (see D4) — no changes to `IChatPipeline` itself, no duplicated model-dispatch logic (Principle IV).

**US2's R1 boundary**: "the user's typed message and attachments remain available for retry" (FR-007) is a client-state concern — the client already holds what it typed; it never needs the server to echo it back. R1's backend contract is the enabling half: a thread-creation failure for one quadrant returns a **distinct, per-quadrant, non-fatal error** (never a generic 500, never silently swallowed) so a future UI can leave the input untouched and let the user retry. The actual preserve-and-retry UI is a follow-up task — consistent with specs 002/004/014 shipping API-only, no Blazor chat UI yet.

## Technical Context

**Language/Version**: C# on **.NET 10** — same solution as specs 002/004/014; new project folders, not new projects.

**Primary Dependencies**: Reuses spec 002's `ICurrentUserAccessor`/`IIdentityHasher`, spec 004's `IChatPipeline`/`IChatThreadStore`/`ChatSendResult` (extended, not duplicated), and spec 014's model catalog (for validating a quadrant's assigned model exists). No new NuGet packages — the SSE fan-in for concurrent quadrant streams uses `System.Threading.Channels` (BCL, already available).

**Storage**: **Azure Cosmos DB** — a third document type, `MultiChatSessionDocument`, in spec 004's existing `chat` container (discriminated by `DocType`, same partition-key pattern), one document per user (per spec.md's Assumptions: "one durable multi-chat session per user"). `ChatThreadModel` gains two new nullable fields (`MultiChatSessionId`, `MultiChatPosition`) — the fields spec.md's Key Entities describe as already-modeled-but-never-populated; this spec is the first to populate them.

**Testing**: **xUnit**; unit tests for quadrant floor/cap enforcement (FR-004/005), on-demand thread creation + immediate persistence (FR-003), and the SSE fan-in merge logic (tagging, non-blocking-on-one-failure); `WebApplicationFactory` integration tests for session restore-after-refresh (SC-001) and the parallel-send endpoint (SC-005) using spec 004's existing fake `IChatCompletionClient`/`IChatThreadStore` seams — no new live-dependency surface.

**Target Platform**: Same as specs 002/004/014 — Linux containers on Azure, fronted by Entra ID.

**Project Type**: Web application — extends the existing layered monolith; adds one new streaming API endpoint (parallel send) plus CRUD endpoints for session/quadrant state. The Blazor multi-chat UI itself is a follow-up task, not built in this pass.

**Performance Goals**: The parallel-send endpoint's total latency is bounded by the *slowest* quadrant's model call, not the sum (SC-005's "unblocked by other quadrants' latency") — enforced by dispatching all quadrants concurrently via `Task.Run`/channel-writer tasks, not sequentially.

**Constraints**: Quadrant count invariant (2–4) enforced server-side on every add/remove, never trusted from a client-supplied count (Principle II); on-demand thread creation persists immediately, before the send proceeds (FR-003) — mirrors spec 004's "no store write before every gate passes" discipline in spirit, inverted (here, persistence must happen eagerly, not be deferred).

**Scale/Scope**: Internal enterprise pilot. R1 tasks US1, US2, US4 (all P1). FR-001–FR-007, FR-010, FR-011; SC-001, SC-002, SC-003, SC-005.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluated against constitution v1.2.1.

| Principle / Constraint | Assessment | Verdict |
|---|---|---|
| **I. Azure-Only, No AWS Vestiges** | Cosmos DB only; no new external dependency. | ✅ PASS |
| **II. Explicit, Server-Side Authorization** | Quadrant count bounds and model assignment are enforced/validated server-side on every mutation; a quadrant's assigned model is validated against spec 014's catalog before dispatch — never trusts a client-supplied model id without that check. | ✅ PASS |
| **III. Fail Loud, Never Fabricate Success** | A thread-creation failure for one quadrant surfaces as a distinct per-quadrant error event, never silently dropped (FR-006) and never masked as a fabricated success for that quadrant. | ✅ PASS |
| **IV. One Implementation Per Concern** | Reuses spec 004's `IChatPipeline` verbatim for every quadrant's send — no parallel/duplicated model-dispatch logic; one `IMultiChatSessionStore`, one fan-in merge implementation. | ✅ PASS |
| **V. Schema-Enforced, Not UI-Enforced, Validation** | Quadrant floor/cap (2–4) and model-allow-list validation live in the Application-layer service, not a client check — there is no UI yet to bypass, and there won't be a second validation path when one is built. | ✅ PASS |
| **VI. Testable, EARS-Style Requirements** | Spec FRs are EARS/`MUST`; each R1-scoped SC maps to a concrete test. | ✅ PASS |
| **Tech Stack Alignment** | Cosmos DB for session state (same category as spec 004's threads/messages); no new stack element introduced. | ✅ PASS |

**Result**: No violations. Complexity Tracking is intentionally empty. Deferring US3 and scoping persona-assignment to schema-only are recorded scope decisions (Summary), not constitution exceptions.

## Project Structure

### Documentation (this feature)

```text
specs/006-multi-chat-session-persistence/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md         # Phase 1 output (/speckit-plan command)
├── contracts/            # Phase 1 output (/speckit-plan command)
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

Extends the existing layered solution (specs 002/004/014) — no new projects.

```text
EnterpriseAIPlatform.sln
src/
├── EnterpriseAIPlatform.Web/
│   └── Endpoints/MultiChat/                # GET/PUT session+quadrant CRUD; POST parallel-send (SSE, tagged per quadrant)
├── EnterpriseAIPlatform.Application/
│   └── Chat/                                # IMultiChatSessionStore (contract); MultiChatSession/-Quadrant value types
│                                            # extend IChatThreadStore.CreateAsync with optional multiChatSessionId/position
├── EnterpriseAIPlatform.Infrastructure/
│   └── Chat/                                # CosmosMultiChatSessionStore; MultiChatDispatcher (fan-out via IChatPipeline,
│                                            # Channel<T>-based fan-in merge, one write-task per quadrant)
└── EnterpriseAIPlatform.Domain/
    └── Chat/                                # MultiChatSession, MultiChatQuadrant; ChatThreadModel gains
                                             # MultiChatSessionId/MultiChatPosition (nullable)
tests/
├── EnterpriseAIPlatform.UnitTests/          # quadrant floor/cap, on-demand creation persistence, fan-in merge/error isolation
├── EnterpriseAIPlatform.IntegrationTests/   # session restore-after-refresh, parallel-send endpoint (fakes, no live Cosmos/Azure)
└── EnterpriseAIPlatform.ArchitectureTests/  # single IMultiChatSessionStore implementation
```

**Structure Decision**: `MultiChat` endpoints are a new peer folder to spec 004's `Chat`/spec 014's `ModelAccess` under `Endpoints/`; the session store and dispatcher live in `Infrastructure/Chat/` alongside spec 004's existing chat stores since they share the same Cosmos container and reuse `IChatPipeline` directly — no new feature-module boundary is warranted for what's fundamentally an extension of spec 004's chat domain.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No constitution violations — this section is intentionally empty.

## Phase 0 & 1 Artifacts

- [research.md](./research.md) — technology decisions (Phase 0)
- [data-model.md](./data-model.md) — entities & validation (Phase 1)
- [contracts/](./contracts/) — service interfaces, route table (Phase 1)
- [quickstart.md](./quickstart.md) — end-to-end validation guide (Phase 1)
