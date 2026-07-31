# Phase 0 Research: Multi-Chat Session Persistence

**Feature**: 006-multi-chat-session-persistence | **Date**: 2026-07-29

Scoped to the R1 subset per plan.md's Summary (US1, US2, US4). Format: Decision / Rationale / Alternatives considered.

---

## D1. Session storage

- **Decision**: A `MultiChatSessionDocument` in spec 004's existing `chat` Cosmos container, discriminated by `DocType`, one document per user, `Id` = a deterministic value derived from the owner's partition key (so "get-or-create" is a single point read, not a query).
- **Rationale**: Assumptions state "one durable multi-chat session per user" — a deterministic id makes "does this user have a session yet" a point read (fast, no query/index needed), consistent with Cosmos best practice for a per-partition singleton. Reuses the container spec 004 already owns rather than provisioning a new one (Principle IV extended to "one container per storage concern where it's genuinely the same domain").
- **Alternatives considered**: A new dedicated container (unnecessary — same partition scheme, same domain, no throughput/schema reason to split); a random GUID id requiring a query to find the user's session (slower, no benefit since there's only ever one).

## D2. Quadrant floor/cap enforcement (FR-004/005)

- **Decision**: `MultiChatSession.Quadrants` is a fixed-shape list; "remove" always targets the highest-position quadrant. If the resulting count would drop below 2, the operation instead **clears** that quadrant's assignment (persona/model/thread all reset to null) and leaves the count at 2. "Add" is rejected outright once count is already 4.
- **Rationale**: The spec's acceptance scenarios describe "removing while at the floor clears rather than deletes" and "adding beyond the cap is rejected" — both are describable as constraints on the *last* quadrant without needing arbitrary-position deletion/renumbering, which the spec's scenarios never actually exercise (no scenario removes a *specific middle* quadrant).
- **Alternatives considered**: Arbitrary-position removal with renumbering (adds complexity — thread/model reassignment across positions — the spec's own scenarios don't require it); client-supplied count with server-side re-validation only (violates Principle II's "never trust client state" more directly than necessary — simpler to make the server the sole mutator of count).

## D3. On-demand thread creation + immediate persistence (FR-003)

- **Decision**: When a quadrant's first send arrives and `Quadrant.ThreadId` is null, the parallel-send endpoint calls spec 004's `IChatThreadStore.CreateAsync` (extended with optional `multiChatSessionId`/`multiChatPosition` parameters — see D5) *before* dispatching to `IChatPipeline`, and immediately persists the new `ThreadId` onto that quadrant via `IMultiChatSessionStore.SaveAsync`, in the same request — not deferred to a background job or the eventual message-store write.
- **Rationale**: FR-003 explicitly requires the association survive "an immediate refresh" — deferring the write (e.g., to whenever the message finishes streaming) would leave a window where a refresh loses the new thread's association even though the thread itself already exists server-side. Persisting eagerly, before the send proceeds, closes that window.
- **Alternatives considered**: Persisting the thread association only after the assistant's response completes (reopens exactly the race FR-003 exists to close — a refresh mid-stream would show an empty quadrant even though a thread now exists orphaned in Cosmos).

## D4. Parallel dispatch + side-by-side rendering without a new orchestration layer (FR-010/011, US4)

- **Decision**: One endpoint, `POST /api/multichat/session/messages`, accepts a single `text`. For every quadrant with a non-null `ModelId`, it (a) on-demand-creates a thread if needed (D3), then (b) calls spec 004's `IChatPipeline.SendMessageAsync` **concurrently** (one `Task` per quadrant, not sequential awaits), and (c) merges each quadrant's independent `IAsyncEnumerable<string>` into **one** SSE response via a `System.Threading.Channels.Channel<QuadrantEvent>` — each written event tagged with its quadrant's position, so the client can route chunks back to the right panel as they interleave. A quadrant whose send fails or blocks writes its own error event to the channel and simply stops contributing further events; it does not fault the channel or block other quadrants' writer tasks.
- **Rationale**: This is the same reasoning spec 014 used for `IModelProviderAdapter` (one interface, provider-specific implementations, no re-derivation) applied here: spec 004's `IChatPipeline` already knows how to gate, resolve access, redact, and stream a single send — multi-chat's only genuinely new behavior is *fanning out* N calls to it and *merging* the results, not reimplementing any part of the send path itself (Principle IV). A single tagged SSE stream is directly testable via `WebApplicationFactory` (same pattern as spec 004's `ChatSendMessageTests`) without inventing a new transport.
- **Alternatives considered**: N separate HTTP requests, one per quadrant, left entirely to a future client to fire concurrently (defers SC-005's "dispatched to all assigned models in parallel" to unwritten client code with no way to verify it server-side); a single response that waits for all quadrants and returns them together (violates FR-011's "one model's latency... does not block another quadrant's response from displaying" — this is exactly the sequential/batched behavior the story rejects).

## D5. Extending `ChatThreadModel` (not duplicating it)

- **Decision**: Add `MultiChatSessionId` and `MultiChatPosition` as nullable fields directly on spec 004's `ChatThreadModel`, and add optional `multiChatSessionId`/`multiChatPosition` parameters to `IChatThreadStore.CreateAsync` (defaulting to null, so every existing spec 004 call site is unaffected).
- **Rationale**: spec.md's Key Entities are explicit that `ChatThreadModel` already anticipates these fields in the original (legacy) system and that this spec is "the first to actually populate and rely on them" — extending the existing entity is exactly what the spec describes, not a new parallel thread-like entity (Principle IV).
- **Alternatives considered**: A separate `MultiChatThreadLink` join entity mapping thread↔quadrant (adds a second lookup/consistency concern for what is, per spec, meant to be two fields *on* the thread itself); duplicating `ChatThreadModel` into a multi-chat-specific variant (direct Principle IV violation — two thread representations that could drift).

## D6. US2's server/client boundary (FR-006/007)

- **Decision**: R1's backend contract is: a thread-creation failure for one quadrant during the parallel-send never produces a generic 500 for the whole request and never gets silently swallowed — it's surfaced as a distinct, per-quadrant error event on the same SSE stream (reusing D4's tagging), leaving every other quadrant's send unaffected. The *client-side* "restore my typed text to the input on error" behavior (FR-007) is not implemented here — no Blazor multi-chat UI exists yet (same boundary specs 002/004/014 already drew).
- **Rationale**: FR-007's guarantee is inherently about client-held state the server never took ownership of in the first place — the server doesn't need to "give back" text it was only ever asked to *use*, not *store on failure*. What the server must guarantee is that failure is legible and scoped (this quadrant, not the whole request), which is exactly what a future UI needs to implement the preserve-and-retry UX correctly.
- **Alternatives considered**: Having the server echo the submitted text back in the error payload "just in case" (redundant — the client already has it; this only matters if the client discarded it, which is a client bug FR-007 warns against, not something the server can fix by echoing).

## D7. Testing strategy (SC-001, SC-002, SC-003, SC-005 — R1 subset)

- **Decision**: xUnit unit tests for quadrant floor/cap (D2), on-demand-creation-persists-immediately (D3), and the fan-in merge's error-isolation behavior (D4/D6 — one quadrant's failure doesn't stop others' events from being written); `WebApplicationFactory` integration tests for session-restore-after-refresh (create session, mutate, fetch again, assert identical state) and the parallel-send endpoint against spec 004's existing fake `IChatCompletionClient`/`IChatThreadStore` (one fake configured to fail for one quadrant, succeed for others, asserting the SSE stream contains both an error tag and successful chunks for the other quadrants).
- **Rationale**: Directly maps to the R1-scoped success criteria; no new live-dependency surface — reuses spec 004's exact fake-substitution pattern (`ChatWebApplicationFactory`-style) rather than inventing a new one.
- **Alternatives considered**: A real multi-browser-tab test harness for SC-001 (spec explicitly scopes multi-tab conflict resolution out — "last write wins," not required by this spec — so a single-client restore-after-refresh test is sufficient).

---

**All NEEDS CLARIFICATION resolved for the R1 scope.** US3 (Chat-Home) is a scope decision recorded in plan.md's Summary, not an open unknown blocking Phase 1.
