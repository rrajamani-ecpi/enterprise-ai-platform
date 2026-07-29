# Implementation Plan: Chat Message Pipeline, Tool Safety & Model Access Control

**Branch**: `004-chat-message-pipeline` | **Date**: 2026-07-29 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/004-chat-message-pipeline/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

This is **Layer 3** of [the sequencing plan](../../docs/spec-sequencing-plan.md) — the first user-facing chat capability, depending on spec 002 (session/role) and spec 014 (model registry/access-gating), both already implemented.

Per [`docs/Release1-MVP Plan.md`](../../docs/Release1-MVP%20Plan.md), **R1 scopes this spec hard**: "P1: send → stream → persist a thread, **plus** the production cross-cutting FRs a pilot requires — reliability wrapper (timeout/retry/circuit-breaker), PII redaction, and Content Safety/guardrails. Defer artifacts, context compression, persona-launch, multimodal." Mapped onto this spec's 10 user stories:

**R1 in scope**: **US1** (message-limit preflight, read-only-thread gate, fail-open, server-authoritative `dataProducts` — FR-001–FR-006) and the **R1 subset of US5** (model-access substitution via spec 014, generic-500-no-leak — FR-020/FR-021/FR-024). Plus three **constitution-mandated additions that aren't spec-004 FRs at all** but are required regardless (Technology Stack & Platform Alignment's Responsible AI + WAF Reliability sections): PII redaction on user text before it reaches the model, an Azure AI Content Safety guardrail at the model boundary, and a timeout/retry/circuit-breaker wrapper around the model call itself (FR-009 already requires this for *tool* calls; R1 extends the same pattern to the model call, since tools aren't in R1).

**Explicitly deferred to R2+ — no design debt, just no R1 caller yet**:
- **US2** (React artifact sandboxing) and **US9** (artifact panel) — R1 ships no artifact generation at all.
- **US3** (share-recipient/expiry enforcement) — spec 018 (Sharing) isn't in R1; there is no share feature yet to secure.
- **US4** (consistent tool-failure shape) — spec 020 (Tools/Extensions) isn't in R1; no tools ship yet.
- **US5's** image-size gate (FR-022, multimodal) and CSV-injection guard (FR-023, no CSV export in R1).
- **US6** (context compression) and **US7** (max-thread-size handling) — explicitly named deferred in the MVP doc.
- **US8** (feedback capture) — depends on spec 017's proxy path (FR-008–FR-010, spec 017 not yet planned).
- **US10** (persona-launch) — spec 009/010 (Personas) aren't in R1.

This plan designs the full spec's data model (so R2 stories slot in without rework) but **tasks only the R1 subset above** — the send → stream → persist vertical, hardened per the constitution.

## Technical Context

**Language/Version**: C# on **.NET 10** — same solution as specs 002/014; new project folders, not new projects.

**Primary Dependencies**: Reuses spec 002's `ICurrentUserAccessor`/`IIdentityHasher`/`ServerActionResponse<T>` and spec 014's `IModelAccessService`/`IModelCatalogService`/`IModelProviderAdapter` (the `AzureFoundryProviderAdapter` built there now gets a real caller). New: **Microsoft.Extensions.Http.Resilience** (timeout/retry/circuit-breaker around the model HTTP call, per the constitution's WAF Reliability pillar and FR-009's existing pattern); a lightweight rule-based PII redactor (no new package — regex-based, per the constitution's explicit fail-closed fallback tier); an Azure AI Content Safety client (plain `HttpClient` call, workload-identity-authenticated, no new SDK dependency to keep the surface small and testable without a live resource).

**Storage**: **Azure Cosmos DB**, per the constitution's Data & Storage guidance naming Cosmos explicitly for "high-throughput, flexible-schema chat/message/thread storage." `ChatThreadModel` and `ChatMessageModel` are separate Cosmos documents (per spec.md's Key Entities), partition-keyed by spec 002's hashed-identity `StoragePartitionKey` — the first real usage of spec 002's `CosmosClientProvider` beyond scaffolding.

**Testing**: **xUnit**; unit tests for preflight ordering/fail-open (SC-001/002), the model-substitution gate (SC-007), and PII redaction; `WebApplicationFactory` integration tests for the send-message endpoint (streaming response, persisted thread/message state) with a **fake `IChatCompletionClient`** substituted for the real Azure/Foundry call — this environment has no live Azure credentials, so the real HTTP dispatch path is exercised for its *shape* (request adaptation, resilience-policy wiring) but never actually invoked in CI, matching spec 014's `AzureFoundryProviderAdapter` testing approach. Cosmos access is tested against the SDK's `CosmosClient`-free repository seam (an in-memory fake), since no Cosmos emulator is available here.

**Target Platform**: Same as specs 002/014 — Linux containers on Azure, fronted by Entra ID via the existing walking skeleton.

**Project Type**: Web application — extends the existing layered monolith; adds a streaming API endpoint. The Blazor chat UI itself is a **follow-up UI task**, not built in this pass — consistent with specs 002/014 shipping API-only (`Home.razor` remains scaffold).

**Performance Goals**: Preflight checks (FR-001) complete before any persistence write, adding negligible latency; the model call itself dominates response time and is streamed token-by-token so first-byte latency isn't blocked on full completion.

**Constraints**: Preflight ordering is absolute (Principle III) — a blocked message must leave zero persistence trace; fail-open only where explicitly documented (message-limit config/counter read failure, FR-005) — Content Safety and PII redaction are NOT fail-open (Security & Compliance Constraints: PII redaction fails closed to regex, never open); no model call happens without first resolving effective access through spec 014 (Principle II).

**Scale/Scope**: Internal enterprise pilot. R1 tasks US1 (full) + US5 (R1 subset) + the three constitution-mandated cross-cutting additions. FR-001–FR-006, FR-020, FR-021, FR-024 plus the PII/Content-Safety/reliability additions; SC-001–SC-003, SC-007.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluated against constitution v1.2.1.

| Principle / Constraint | Assessment | Verdict |
|---|---|---|
| **I. Azure-Only, No AWS Vestiges** | Cosmos DB, Azure AI Content Safety, Azure/Foundry model endpoint via spec 014's adapter, workload identity throughout. | ✅ PASS |
| **II. Explicit, Server-Side Authorization** | Model selection resolved server-side through spec 014's `IModelAccessService` on every send (FR-020/021) — never trusts a client-supplied model id; preflight checks run server-side before any persistence (FR-001). | ✅ PASS |
| **III. Fail Loud, Never Fabricate Success** | Preflight fail-open is scoped ONLY to the documented message-limit config/counter case (FR-005); PII redaction and Content Safety are explicitly NOT fail-open (regex fallback is itself the fail-closed behavior, per Security & Compliance Constraints); unhandled exceptions return a generic 500, never a fabricated success (FR-024). | ✅ PASS |
| **IV. One Implementation Per Concern** | One preflight pipeline, one `IPiiRedactor`, one `IContentSafetyGuard`, one `IChatCompletionClient` per provider (reusing spec 014's one-adapter-per-provider pattern) — architecture-tested. | ✅ PASS |
| **V. Schema-Enforced, Not UI-Enforced, Validation** | Per-message/daily caps, read-only-thread gate, and model substitution are enforced in the Application-layer pipeline, not a client check — there is no chat UI yet in R1, so this is the only enforcement layer that will ever exist until one is built. | ✅ PASS |
| **VI. Testable, EARS-Style Requirements** | Spec FRs are EARS/`MUST`; each R1-scoped SC maps to a concrete xUnit/integration test. | ✅ PASS |
| **Tech Stack Alignment** | Cosmos DB for chat/message storage, `Microsoft.Extensions.Http.Resilience` for the WAF Reliability pillar, Azure AI Content Safety for Responsible AI — matches the constitution's Technology Stack section directly. | ✅ PASS |
| **Security & Compliance Constraints** | PII redaction on by default, fails closed to regex (never open) on ML-detection failure — R1 implements the regex tier as the sole tier, documented as such, not a silent gap; identity for persistence is the server-derived hashed identity, never client-supplied. | ✅ PASS |

**Result**: No violations. Complexity Tracking is intentionally empty. Deferring 8 of 10 user stories to R2+ is a **scope** decision (recorded in Summary, matching the MVP doc), not a constitution exception.

## Project Structure

### Documentation (this feature)

```text
specs/004-chat-message-pipeline/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md         # Phase 1 output (/speckit-plan command)
├── contracts/            # Phase 1 output (/speckit-plan command)
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

Extends the existing layered solution (specs 002/014) — no new projects.

```text
EnterpriseAIPlatform.sln
src/
├── EnterpriseAIPlatform.Web/
│   └── Endpoints/Chat/                     # POST /api/chat/threads, POST /api/chat/threads/{id}/messages (SSE stream)
├── EnterpriseAIPlatform.Application/
│   └── Chat/                                # IChatThreadStore, IChatMessageStore, IChatPipeline (preflight + prompt
│                                            # assembly + orchestration), IPiiRedactor, IContentSafetyGuard,
│                                            # IChatCompletionClient (contract; spec 014's IModelProviderAdapter feeds it)
├── EnterpriseAIPlatform.Infrastructure/
│   ├── Chat/                                # Cosmos-backed thread/message stores, ChatPipeline implementation
│   ├── Redaction/                           # RegexPiiRedactor (fail-closed, sole R1 tier)
│   ├── Safety/                              # AzureContentSafetyGuard (workload identity, lazy — skips if unconfigured
│                                            # in dev, fails startup validation if unconfigured in Production)
│   └── ModelProviders/                      # AzureFoundryChatCompletionClient (wraps spec 014's adapter +
│                                            # Microsoft.Extensions.Http.Resilience around the actual HTTP call)
└── EnterpriseAIPlatform.Domain/
    └── Chat/                                # ChatThreadModel, ChatMessageModel, PreflightResult value objects
tests/
├── EnterpriseAIPlatform.UnitTests/          # preflight ordering/fail-open (SC-001/002), model substitution (SC-007),
│                                            # PII redaction, resilience-policy wiring
├── EnterpriseAIPlatform.IntegrationTests/   # send-message endpoint (streamed response + persisted state) via a
│                                            # fake IChatCompletionClient and an in-memory thread/message store fake
└── EnterpriseAIPlatform.ArchitectureTests/  # single IPiiRedactor/IContentSafetyGuard/IChatCompletionClient-per-provider
```

**Structure Decision**: Reuse the layered monolith — `Chat` is a new peer feature-module folder to spec 014's `ModelAccess`, following the same Domain/Application/Infrastructure split so the chat pipeline's preflight/redaction/safety logic stays server-side and testable without a live Cosmos/Azure dependency, matching specs 002/014's established pattern.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No constitution violations — this section is intentionally empty.

## Phase 0 & 1 Artifacts

- [research.md](./research.md) — technology decisions (Phase 0)
- [data-model.md](./data-model.md) — entities & validation (Phase 1)
- [contracts/](./contracts/) — service interfaces, route table (Phase 1)
- [quickstart.md](./quickstart.md) — end-to-end validation guide (Phase 1)
