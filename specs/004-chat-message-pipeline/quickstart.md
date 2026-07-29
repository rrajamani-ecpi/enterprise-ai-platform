# Quickstart: Validating the Chat Message Pipeline (R1 subset)

**Feature**: 004-chat-message-pipeline | **Date**: 2026-07-29

Validation guide for the R1-scoped subset (US1 full, US5 R1 subset, plus PII redaction / Content Safety / reliability wrapper). US2–US4, US6–US10 and US5's image/CSV pieces are R2 — not exercised here.

## Prerequisites

- Specs 002 and 014's walking skeleton running (session/role, model registry/access-gating).
- An Azure Cosmos DB account (or emulator) for `ChatThreadModel`/`ChatMessageModel` — reuses spec 002's `Cosmos:AccountEndpoint` config.
- For a full live run: a configured `ModelProviders:AzureFoundry:Endpoint` (spec 014) and `ContentSafety:Endpoint` (spec 004), both workload-identity-authenticated. **Neither is required for the automated test suite** — tests substitute fakes for both (no live Azure dependency in this environment).

## Setup

```bash
dotnet restore
dotnet build EnterpriseAIPlatform.sln
dotnet run --project src/EnterpriseAIPlatform.Web
```

## Validation scenarios (one per in-scope story/requirement)

| Story/FR | Steps | Expected (pass) |
|---|---|---|
| **US1 · Daily cap** (FR-004) | Configure a low daily cap via spec 014's message-limit config; exceed it; send another message. | `402 DAILY_MESSAGE_LIMIT_EXCEEDED` with `resetsAt`; no thread/message document created (SC-001). |
| **US1 · Read-only thread** (FR-002) | Attempt to send to a thread whose `Version != "v3"` (simulate by direct store manipulation, since R1 never creates a non-v3 thread itself). | `409 THREAD_READ_ONLY`; no record touched (SC-001). |
| **US1 · Fail-open** (FR-005) | Simulate the message-limit config/counter store being unreachable; send a message. | The message is allowed through, not blocked (SC-002). |
| **US1 · Server-authoritative dataProducts** (FR-006) | Send a request with a client-supplied `dataProducts` array differing from the thread's stored value (empty in R1). | The server's stored (empty) value is used; the client's value is discarded (SC-003). |
| **US5 · Model substitution** (FR-020/021) | Request a model outside the caller's resolved allow-list (per spec 014's config). | The configured fallback model is used instead; the response never reflects the requested, disallowed model (SC-007). |
| **Content Safety** (constitution) | Send text that the configured Content Safety guard flags (or, with no endpoint configured in dev, confirm the message proceeds with a logged warning). | Blocked with `400 CONTENT_BLOCKED` in a configured environment; allowed-with-warning in unconfigured dev. |
| **PII redaction** (FR-008) | Send a message containing an email address or phone number. | The model-bound copy has the PII pattern redacted; the persisted `ChatMessageModel.Content` retains the original, unredacted text. |
| **Reliability wrapper** (constitution WAF) | Inspect `AzureFoundryChatCompletionClient`'s HTTP pipeline configuration. | A `Microsoft.Extensions.Http.Resilience` standard resilience handler (timeout/retry/circuit-breaker) wraps the outbound call. |
| **Generic 500** (FR-024) | Force an unhandled exception in the pipeline (e.g., a fake store throwing). | The client receives a generic `500` with no internal error detail. |

## Automated test commands

```bash
dotnet test tests/EnterpriseAIPlatform.UnitTests          # preflight ordering/fail-open, model substitution, redaction
dotnet test tests/EnterpriseAIPlatform.IntegrationTests   # send-message endpoint: streaming + persistence, via fakes
dotnet test tests/EnterpriseAIPlatform.ArchitectureTests  # single IPiiRedactor/IContentSafetyGuard/IChatCompletionClient
```

## Done when

- [ ] All R1-scoped scenarios above pass.
- [ ] All three test projects green, covering SC-001, SC-002, SC-003, SC-007.
- [ ] Architecture test confirms exactly one implementation per pipeline-stage interface (Principle IV).
- [ ] SC-004–SC-006, SC-008–SC-016 are explicitly recorded as deferred to R2 (US2–US4, US6–US10, US5's image/CSV pieces) — not silently skipped.
