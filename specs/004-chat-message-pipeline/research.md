# Phase 0 Research: Chat Message Pipeline, Tool Safety & Model Access Control

**Feature**: 004-chat-message-pipeline | **Date**: 2026-07-29

Scoped to the R1 subset per plan.md's Summary (US1 full, US5 R1 subset, plus the three constitution-mandated additions). Format: Decision / Rationale / Alternatives considered.

---

## D1. Thread/message storage

- **Decision**: **Azure Cosmos DB**, two document types — `ChatThreadModel` (metadata) and `ChatMessageModel` (the message list) — partition-keyed by spec 002's `StoragePartitionKey` (hashed owner identity), reusing spec 002's `CosmosClientProvider`.
- **Rationale**: The constitution names this exact category ("chat/message/thread storage") for Cosmos, distinct from spec 014's SQL usage for admin/system config. Splitting thread metadata from the message list mirrors spec.md's own Key Entities (`ChatThreadModel` vs `ChatMessageModelV2` are already described as separate documents).
- **Alternatives considered**: Azure SQL (constitution explicitly prefers Cosmos for this flexible-schema, high-throughput category); a single combined document (would grow unbounded and contradicts the spec's own two-entity model, plus complicates the Story 7 max-size handling this plan defers but shouldn't architecturally block).

## D2. Preflight ordering and fail-open scope (FR-001–FR-005, US1)

- **Decision**: A single `IChatPipeline.SendMessageAsync` runs, in order: (1) thread-version gate (FR-002, reject `!= v3`), (2) message-limit preflight (FR-003/004, fail-open per FR-005 only on config/counter-store read failure), (3) server-authoritative `dataProducts` override (FR-006, discard client value — no-op in R1 since data products aren't wired yet, but the seam exists so spec 019 has a slot). No thread/message document is created or modified until all of (1)–(3) pass.
- **Rationale**: FR-001 requires zero persistence trace for a blocked message — ordering the gate checks before any Cosmos write is the only way to guarantee that. Scoping fail-open narrowly (only the read-failure case, never the cap-exceeded case) matches FR-005's precise wording and the constitution's fail-open carve-out, which names this exact scenario.
- **Alternatives considered**: Checking caps after creating a "pending" thread/message and rolling back on rejection (violates FR-001's "leaves no trace" requirement — a rollback still touched the store); fail-open on any preflight error (over-broad; would fail open on the version-gate check too, which must fail closed).

## D3. Model-access integration (FR-020/021, US5 R1 subset)

- **Decision**: Before any model call, `IChatPipeline` calls spec 014's `IModelAccessService.GetAvailableModelsAsync(caller)`; if the request's model id isn't in the returned set, substitute `SystemModelConfig.FallbackModelId` (resolved via spec 014's `ISystemModelConfigCache`) rather than rejecting the request.
- **Rationale**: Reuses spec 014's already-implemented, already-tested access computation verbatim (Principle IV) — spec 004 doesn't re-derive role/allow-list logic, it consumes the one existing implementation. Matches FR-021's "substitute the configured default model" exactly.
- **Alternatives considered**: Re-deriving allow-list logic inside the chat pipeline (duplicates spec 014, violates Principle IV); rejecting the request outright instead of substituting (contradicts FR-021's explicit substitution behavior).

## D4. Model invocation & streaming (send → stream → persist)

- **Decision**: `IChatCompletionClient.StreamCompletionAsync(...)` returns `IAsyncEnumerable<string>` chunks. The R1 implementation, `AzureFoundryChatCompletionClient`, calls spec 014's `AzureFoundryProviderAdapter.AdaptRequestAsync` for the request shape + workload-identity auth header, then makes the actual HTTP call wrapped in a `Microsoft.Extensions.Http.Resilience` policy (timeout + retry + circuit breaker), and adapts the response via `AdaptResponseAsync`. The `POST /api/chat/threads/{id}/messages` endpoint streams chunks to the caller as Server-Sent Events (`text/event-stream`) and persists the assembled full message after the stream completes.
- **Rationale**: Reuses spec 014's adapter directly rather than duplicating request/response shaping (Principle IV) — spec 014's adapter was explicitly built with "the actual HTTP dispatch is spec 004's concern" in mind. SSE over a minimal API endpoint is directly testable via `WebApplicationFactory` (unlike a Blazor-component-only streaming approach), and remains compatible with a future Blazor Interactive Server page simply subscribing to the same stream.
- **Alternatives considered**: Building the chat UI directly in Blazor now and skipping a testable HTTP surface (this plan explicitly defers the UI — see Constraints); using a raw non-resilient `HttpClient` call (violates the constitution's WAF Reliability pillar, which FR-009 already establishes as the pattern for tool calls).

## D5. No live Azure test target — testing strategy for the model call

- **Decision**: Tests substitute a fake `IChatCompletionClient` (NSubstitute) that yields deterministic chunks; the real `AzureFoundryChatCompletionClient` is unit-tested only for its *request adaptation and resilience-policy wiring*, never invoked against a live endpoint in CI — identical testing posture to spec 014's `AzureFoundryProviderAdapter`.
- **Rationale**: This environment has no Azure credentials or network path to a real Foundry endpoint. Testing the seam (adaptation + resilience config) rather than the live call keeps the pipeline's correctness (preflight, redaction, persistence, streaming plumbing) fully verifiable without a live dependency, consistent with D5's precedent in spec 014.
- **Alternatives considered**: Skipping model-call tests entirely (leaves the pipeline's happy-path untested); standing up a mock HTTP server to simulate the Foundry endpoint (adds complexity without adding confidence beyond what a fake `IChatCompletionClient` already provides for pipeline-level tests).

## D6. PII redaction (constitution addition, not a spec-004 FR by number but referenced in FR-008/Edge Cases)

- **Decision**: `IPiiRedactor.Redact(string userText)` — R1 ships exactly one implementation, `RegexPiiRedactor`, applying rule-based patterns (email, phone, SSN-shaped sequences, credit-card-shaped sequences) to user-authored text only, before it is included in the assembled prompt. Never applied to persisted history, assistant output, or documents (FR-008).
- **Rationale**: The constitution's Security & Compliance Constraints describe a two-tier design (ML-based primary, regex fallback) but explicitly names the regex tier as the required fail-closed behavior. Without a provisioned Azure PII-detection service in this environment, shipping the fail-closed tier as the sole R1 implementation is honest and correct — an ML tier is a strict *addition* behind the same `IPiiRedactor` interface, not a redesign, when a provider is available.
- **Alternatives considered**: Shipping no redaction until an ML service is available (violates "PII redaction MUST be on by default," a hard constitution requirement, not an R1-optional item); building a second interface for the "ML-based" path now with no implementation (adds an unused seam without a caller — YAGNI).

## D7. Content Safety guardrail (constitution addition)

- **Decision**: `IContentSafetyGuard.CheckAsync(string text)` — R1 implementation `AzureContentSafetyGuard` calls Azure AI Content Safety's REST API (plain `HttpClient`, workload-identity-authenticated, no new SDK dependency) on user-authored text before it reaches the model. `ContentSafetyOptions.Endpoint` is validated at startup: unconfigured in `Production` fails app boot (fail loud, Principle III); unconfigured in `Development` logs a warning and allows the message through, so local dev doesn't require a live Content Safety resource — mirroring spec 002's `PlatformAuthenticationOptions` dev/prod validation split.
- **Rationale**: The constitution requires Content Safety at "every model-facing boundary," but (unlike PII redaction) provides no explicit fail-open carve-out for it — treating an *unconfigured Production* deployment as a boot-time failure (rather than a silent bypass) satisfies "fail loud," while a documented dev-mode bypass avoids blocking local development on a resource this sandbox can't provision.
- **Alternatives considered**: Fail-open silently whenever unreachable, in every environment (violates Principle III — an outage would silently disable a Responsible-AI control); require it configured in every environment including dev/test (blocks all local development and this implementation's own test suite on a live Azure resource that doesn't exist here).

## D8. Reliability wrapper scope (constitution WAF Reliability pillar)

- **Decision**: `Microsoft.Extensions.Http.Resilience`'s standard resilience handler (timeout + retry with jittered backoff + circuit breaker) wraps only the outbound HTTP call inside `AzureFoundryChatCompletionClient` — not the Cosmos calls (the Cosmos SDK has its own built-in retry policy) and not the Content Safety call in R1 (kept simple; a fast-follow, not blocking the send→stream→persist vertical).
- **Rationale**: FR-009 already establishes timeout/retry/circuit-breaker as the required pattern for tool calls; the constitution's WAF Reliability pillar extends the same expectation to model calls. Scoping the *new* wrapper to just the model HTTP call (the highest-risk, most failure-prone external call in this vertical) keeps R1 focused without re-plumbing Cosmos's already-resilient client.
- **Alternatives considered**: A custom hand-rolled retry loop (reinvents `Microsoft.Extensions.Http.Resilience`, a first-party, already-constitution-aligned package); wrapping Content Safety too in R1 (adds scope without a corresponding SC to verify against).

## D9. Testing strategy (SC-001–SC-003, SC-007 — R1 subset)

- **Decision**: xUnit unit tests for preflight ordering/fail-open (D2) and the model-substitution gate (D3); `WebApplicationFactory` integration tests for the send-message endpoint against an in-memory thread/message store fake and a fake `IChatCompletionClient` (D5); an architecture test asserting exactly one `IPiiRedactor`/`IContentSafetyGuard`/`IChatCompletionClient`-per-provider implementation.
- **Rationale**: Maps directly to the R1-scoped success criteria; avoids any live-Cosmos or live-Azure-model dependency in CI, matching every prior spec's testing posture in this codebase.
- **Alternatives considered**: Cosmos emulator in CI (not available in this sandbox; would also slow every future test run for a capability most tests don't need to exercise at the storage layer).

---

**All NEEDS CLARIFICATION resolved for the R1 scope.** Deferred-to-R2 items (US2–US4, US6–US10, and US5's image/CSV pieces) are scope decisions recorded in plan.md's Summary, not open unknowns blocking Phase 1.
