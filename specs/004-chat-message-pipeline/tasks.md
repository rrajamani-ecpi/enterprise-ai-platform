# Tasks: Chat Message Pipeline, Tool Safety & Model Access Control (R1 subset)

**Input**: Design documents from `/specs/004-chat-message-pipeline/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)
**Tests**: Included — this spec's R1-scoped success criteria (SC-001–SC-003, SC-007) are defined as test suites, and Constitution Principle VI requires falsifiable per-story tests.

**Release 1 scope note** (per [`docs/Release1-MVP Plan.md`](../../docs/Release1-MVP%20Plan.md) and `plan.md`'s Summary): this tasks.md covers **US1 (full) and US5 (R1 subset: model substitution + generic 500)**, plus three **constitution-mandated additions that have no user-story number** — PII redaction, an Azure AI Content Safety guardrail, and a reliability wrapper around the model call. **Explicitly deferred to R2 — no tasks generated here, not forgotten**:
- **US2** (React artifact sandboxing), **US9** (artifact panel) — no artifact generation ships in R1.
- **US3** (share-recipient/expiry) — spec 018 (Sharing) isn't in R1.
- **US4** (consistent tool-failure shape) — spec 020 (Tools) isn't in R1.
- **US5's** image-size gate (FR-022) and CSV-injection guard (FR-023) — no multimodal/CSV export in R1.
- **US6** (context compression), **US7** (max-thread-size handling) — named deferred in the MVP doc.
- **US8** (feedback capture) — blocked on spec 017 (not yet planned).
- **US10** (persona-launch) — spec 009/010 (Personas) aren't in R1.

## Implementation status (2026-07-29)

All 33 R1 tasks complete. `dotnet build` clean; **119/119 tests pass** across the whole solution (12 architecture, 76 unit, 31 integration — up from 002+014's 86, +33 for this spec). No live Azure/Cosmos dependency required — Cosmos-backed stores and the real model/Content-Safety calls are swapped for in-memory fakes in tests (see research.md D5/D9).

A pre-existing walking-skeleton bug surfaced while building the send-message endpoint: `StreamWriter { AutoFlush = true }` calls a **synchronous** `Flush()` internally, which `TestServer` (and some hosts) disallow on the response body, producing a spurious 500 on every streamed response. Fixed by flushing explicitly and asynchronously (`await writer.FlushAsync()`) after each chunk in `ChatEndpoints.StreamResult`.

## Path Conventions (from plan.md — extends specs 002/014's layered solution, no new project)

- `src/EnterpriseAIPlatform.Domain/Chat/`, `.Application/Chat/`, `.Infrastructure/{Chat,Redaction,Safety,ModelProviders}/`, `.Web/Endpoints/Chat/`
- `tests/EnterpriseAIPlatform.UnitTests/`, `.IntegrationTests/`, `.ArchitectureTests/`

---

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 Add `Microsoft.Extensions.Http.Resilience` NuGet dependency to `EnterpriseAIPlatform.Infrastructure`
- [X] T002 [P] Add `ContentSafety:Endpoint` and a `Cosmos:ChatContainerName` (default `"chat"`) placeholder (no secrets) to `appsettings.json` in `src/EnterpriseAIPlatform.Web/`

**Checkpoint**: Solution still builds with the new dependency restored.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ No user story work begins until this phase is complete.**

- [X] T003 [P] Create `ChatThreadModel`, `ChatMessageModel`, `PreflightResult`, `PiiRedactionResult`, `ContentSafetyVerdict` in `src/EnterpriseAIPlatform.Domain/Chat/`
- [X] T004 [P] Declare contracts `IChatPipeline`, `IChatThreadStore`, `IChatMessageStore`, `IPiiRedactor`, `IContentSafetyGuard`, `IChatCompletionClient` (and `ChatSendResult`/`PreflightRejectionCode`) in `src/EnterpriseAIPlatform.Application/Chat/`
- [X] T005 Implement Cosmos-backed `IChatThreadStore`/`IChatMessageStore` (single `chat` container, `ChatThreadModel`/`ChatMessageModel` discriminated by a `DocType` field, partitioned by spec 002's `StoragePartitionKey`) in `src/EnterpriseAIPlatform.Infrastructure/Chat/`
- [X] T006 Implement `ContentSafetyOptions` with startup validation (fails app boot if unconfigured in Production; logs and allows through if unconfigured in Development) in `src/EnterpriseAIPlatform.Infrastructure/Safety/` and `src/EnterpriseAIPlatform.Web/Program.cs`
- [X] T007 Create `AddChatInfrastructure` DI registration extension in `src/EnterpriseAIPlatform.Infrastructure/DependencyInjection/`

**Checkpoint**: Entities/contracts compile; Cosmos stores are unit-testable via an in-memory fake seam.

---

## Phase 3: User Story 1 — Blocked or invalid messages leave no trace (Priority: P1) 🎯 MVP

**Goal**: A blocked message (cap exceeded, read-only thread) never creates/modifies a persistence record; a message-limit infra failure fails open; client-supplied `dataProducts` never overrides the server's stored value.
**Independent Test**: Exceed the daily cap and confirm zero new/modified records; send to a non-`v3` thread and confirm 409 with no mutation; simulate a config/counter read failure and confirm the message is allowed through (SC-001/002/003).

- [X] T008 [P] [US1] Unit tests: non-`v3` thread rejected with `ThreadReadOnly` before any store write (SC-001) in `tests/EnterpriseAIPlatform.UnitTests/`
- [X] T009 [P] [US1] Unit tests: per-message cap (FR-003) and daily cap (FR-004) rejections produce zero store writes (SC-001); a simulated counter-store read failure allows the message through (SC-002, FR-005) in `tests/EnterpriseAIPlatform.UnitTests/`
- [X] T010 [P] [US1] Unit test: a client-supplied `dataProducts` value differing from the thread's stored (empty) value is discarded (SC-003, FR-006) in `tests/EnterpriseAIPlatform.UnitTests/`
- [X] T011 [US1] Implement the daily message counter (Redis-backed via spec 014's `IDistributedCache` registration, keyed per owner per America/New_York day, fail-open on read failure) in `src/EnterpriseAIPlatform.Infrastructure/Chat/`
- [X] T012 [US1] Implement the message-limit preflight check (per-message cap, daily cap, fail-open) reusing spec 014's `IMessageLimitConfigService` for cap values, in `src/EnterpriseAIPlatform.Infrastructure/Chat/`
- [X] T013 [US1] Implement the thread-version read-only gate (FR-002) and the `dataProducts` server-authoritative override (FR-006, no-op returning the thread's stored empty array) in `src/EnterpriseAIPlatform.Infrastructure/Chat/`
- [X] T014 [US1] Implement `IChatPipeline.SendMessageAsync`'s gate-ordering (version → preflight → dataProducts override), returning `ChatSendResult.Rejected` before any store call on failure, in `src/EnterpriseAIPlatform.Infrastructure/Chat/`
- [X] T015 [US1] Implement `POST /api/chat/threads` and the rejection-mapping half of `POST /api/chat/threads/{id}/messages` (maps `ChatSendResult.Rejected` to 409/400/402) in `src/EnterpriseAIPlatform.Web/Endpoints/Chat/`

**Checkpoint**: US1 fully functional and independently testable (MVP) — no model call or persistence happens yet for the happy path (that's Phase 7).

---

## Phase 4: User Story 5 (R1 subset) — Model access stays a non-bypassable gate (Priority: P3, R1-critical)

**Goal**: A requested model outside the caller's resolved allow-list is silently substituted with the configured default; any unhandled exception yields a generic 500.
**Independent Test**: Request a disallowed model directly via the API and confirm the fallback model is used, never the requested one (SC-007); force an unhandled exception and confirm no internal detail leaks (FR-024).

- [X] T016 [P] [US5] Unit test: a requested model outside the caller's resolved allow-list (per spec 014) results in the configured fallback model being used (SC-007) in `tests/EnterpriseAIPlatform.UnitTests/`
- [X] T017 [P] [US5] Integration test: a fake pipeline dependency throws → the endpoint returns a generic 500 with no internal error detail (FR-024) in `tests/EnterpriseAIPlatform.IntegrationTests/`
- [X] T018 [US5] Implement the model-access resolution step in `IChatPipeline` using spec 014's `IModelAccessService`/`ISystemModelConfigCache` fallback (FR-020/021) in `src/EnterpriseAIPlatform.Infrastructure/Chat/`
- [X] T019 [US5] Add a top-level exception boundary around `POST /api/chat/threads/{id}/messages` mapping any unhandled exception to a generic 500 (FR-024) in `src/EnterpriseAIPlatform.Web/Endpoints/Chat/`

**Checkpoint**: US1 + US5 independently testable together.

---

## Phase 5: PII Redaction (constitution addition, FR-008)

**Goal**: User-authored text is redacted before reaching the model; the persisted copy stays original.
**Independent Test**: Send text containing an email/phone number; confirm the model-bound copy is redacted and the persisted `ChatMessageModel.Content` is not.

- [X] T020 [P] Unit tests: `RegexPiiRedactor` redacts email/phone/SSN-shaped/credit-card-shaped patterns from the model-bound copy in `tests/EnterpriseAIPlatform.UnitTests/`
- [X] T021 Implement `IPiiRedactor`/`RegexPiiRedactor` in `src/EnterpriseAIPlatform.Infrastructure/Redaction/`
- [X] T022 Wire redaction into `IChatPipeline` (model-bound copy only, never the persisted `ChatMessageModel.Content`) in `src/EnterpriseAIPlatform.Infrastructure/Chat/`

**Checkpoint**: Redaction independently testable; persisted content unaffected.

---

## Phase 6: Content Safety Guardrail (constitution addition)

**Goal**: User text is checked against Azure AI Content Safety before the model call; unconfigured Production fails startup, unconfigured Development allows through with a warning.
**Independent Test**: Configure a guard that flags text and confirm the send is blocked before any model call; confirm dev-mode-unconfigured behavior.

- [X] T023 [P] Unit tests: `ContentSafetyOptions` validation — unconfigured + Production fails startup; unconfigured + Development passes with a logged warning in `tests/EnterpriseAIPlatform.UnitTests/`
- [X] T024 [P] Unit test: `AzureContentSafetyGuard` blocks when the (faked) Content Safety API signals unsafe, allows when safe, in `tests/EnterpriseAIPlatform.UnitTests/`
- [X] T025 Implement `IContentSafetyGuard`/`AzureContentSafetyGuard` (workload-identity `HttpClient` call) in `src/EnterpriseAIPlatform.Infrastructure/Safety/`
- [X] T026 Wire the Content Safety check into `IChatPipeline` — runs on the original user text, before redaction and before the model call; a block returns `ChatSendResult.ContentBlocked` in `src/EnterpriseAIPlatform.Infrastructure/Chat/`

**Checkpoint**: Content Safety independently testable; ordering (safety check → redaction → model call) verified.

---

## Phase 7: Model Invocation, Streaming & Persistence (the send → stream → persist vertical)

**Goal**: The pipeline actually calls the model (via spec 014's adapter, wrapped in a reliability policy), streams the response as SSE, and persists both messages after the stream completes.
**Independent Test**: Send a message through a fake `IChatCompletionClient`; confirm the client receives streamed SSE chunks and the full thread/message state is persisted afterward.

- [X] T027 [P] Unit test: `AzureFoundryChatCompletionClient`'s `HttpClient` is configured with a `Microsoft.Extensions.Http.Resilience` standard resilience handler (timeout/retry/circuit-breaker) in `tests/EnterpriseAIPlatform.UnitTests/`
- [X] T028 [P] Integration test: `POST /api/chat/threads/{id}/messages` against a fake `IChatCompletionClient` streams the expected SSE chunks and persists the assembled user + assistant `ChatMessageModel` after completion in `tests/EnterpriseAIPlatform.IntegrationTests/`
- [X] T029 Implement `IChatCompletionClient`/`AzureFoundryChatCompletionClient` (uses spec 014's `IModelProviderAdapter` for request/response shaping + the resilience handler around the actual HTTP call) in `src/EnterpriseAIPlatform.Infrastructure/ModelProviders/`
- [X] T030 Wire streaming + persistence into `IChatPipeline`/the endpoint: stream chunks to the caller as `text/event-stream`, then persist the user message and the assembled assistant message via `IChatMessageStore` in `src/EnterpriseAIPlatform.Infrastructure/Chat/` and `src/EnterpriseAIPlatform.Web/Endpoints/Chat/`

**Checkpoint**: All R1-scoped stories (US1, US5-subset) plus the constitution additions are independently functional and now form one working send→stream→persist vertical.

---

## Phase 8: Polish & Cross-Cutting

- [X] T031 [P] Architecture test asserting exactly one implementation each of `IChatPipeline`, `IPiiRedactor`, `IContentSafetyGuard`, and `IChatCompletionClient` in `tests/EnterpriseAIPlatform.ArchitectureTests/`
- [X] T032 [P] Update the solution README with spec 004's endpoints and the explicit R1 (US1 + US5-subset + PII/Content-Safety/reliability) vs. R2 (US2–US4, US6–US10, US5's image/CSV) scope note
- [X] T033 Verify SC-001, SC-002, SC-003, SC-007 are each covered by a passing test; confirm SC-004–SC-006 and SC-008–SC-016 are explicitly recorded as deferred to R2, not silently skipped

**SC coverage map (verified 2026-07-29, 119/119 tests passing across the solution):**
| SC | Covered by |
|---|---|
| SC-001 | `ChatPipelineTests` (non-v3 thread, per-message cap, daily cap — all zero-store-write on rejection) |
| SC-002 | `ChatPipelineTests` (config-unreadable and counter-unreadable fail-open cases) |
| SC-003 | `ChatPipelineTests.SendMessageAsync_HasNoDataProductsParameter_ClientCanNeverSupplyOne` (structural — R1 has no client-supplied dataProducts input at all) |
| SC-004 | **Deferred to R2** — no artifacts ship in R1 (US2) |
| SC-005 | **Deferred to R2** — no sharing feature ships in R1 (US3) |
| SC-006 | **Deferred to R2** — no tools ship in R1 (US4) |
| SC-007 | `ChatPipelineTests.RequestedModel_OutsideAllowList_IsSubstitutedWithFallback` |
| SC-008 | **Deferred to R2** — no multimodal/image upload in R1 (US5 subset) |
| SC-009–SC-011 | **Deferred to R2** — context compression / max-thread-size not built in R1 (US6/US7) |
| SC-012 | **Deferred to R2** — feedback capture blocked on spec 017 (US8) |
| SC-013–SC-014 | **Deferred to R2** — no artifact panel in R1 (US9) |
| SC-015–SC-016 | **Deferred to R2** — no persona-launch in R1 (US10) |
| *(unnumbered)* FR-008 PII redaction | `RegexPiiRedactorTests` |
| *(unnumbered)* Content Safety guardrail | `ContentSafetyOptionsValidationTests`, `AzureContentSafetyGuardTests`, `ChatPipelineTests.ContentSafetyBlock_PreventsModelCall_AndStoreWrite` |
| *(unnumbered)* Reliability wrapper | `AzureFoundryChatCompletionClientResilienceTests` |
| *(unnumbered)* FR-024 generic 500 | `ChatSendMessageTests.SendMessage_UnhandledException_Returns500_WithNoInternalDetail` |
| *(unnumbered)* send→stream→persist happy path | `ChatSendMessageTests.SendMessage_HappyPath_StreamsChunks_AndPersistsBothMessages` |

---

## Dependencies & Execution Order

- **Setup** → **Foundational** block everything.
- **US1** (Phase 3) depends only on Foundational; it is the MVP and doesn't require the model call to exist yet (rejections happen before any model invocation).
- **US5-subset** (Phase 4) depends on Foundational + spec 014's already-implemented `IModelAccessService`.
- **PII Redaction** (Phase 5) and **Content Safety** (Phase 6) are independent of each other and of US1/US5, but both must land before Phase 7 wires them into the live call ordering.
- **Phase 7** (model invocation/streaming/persistence) depends on US1 (gate ordering), US5-subset (resolved model), Phase 5 (redaction), and Phase 6 (safety check) all being in place — it's the integration point.
- Polish last.
- **Deferred to R2, not sequenced here**: US2–US4, US6–US10, and US5's image/CSV pieces (each depends on a capability — artifacts, sharing, tools, compression, feedback proxy, personas, multimodal, CSV export — that isn't in R1 at all).

**Parallel opportunities**: T001/T002; T003/T004; all `[P]` test tasks within a phase; Phases 5 and 6 can proceed in parallel once Foundational lands; Phase 3 and Phase 4 can largely proceed in parallel too (both only need Foundational + spec 014).

## Implementation Strategy

Deliver **US1 first as the MVP checkpoint** (rejection paths need no model call at all, so they're the cheapest slice to prove end-to-end), then US5-subset (model resolution), then PII/Content-Safety (Phases 5/6, independent of each other), then Phase 7 wires everything into one live send→stream→persist path. Keep the architecture test (T031) green throughout to prevent duplication regressions (Principle IV). Do not begin any US2–US4/US6–US10 work against this tasks.md — that is explicitly R2 scope requiring its own `/speckit-tasks` pass once R1 ships.
