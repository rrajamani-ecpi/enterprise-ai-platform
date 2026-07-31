# Implementation Plan: Feedback, Changelog & Health Probes

**Branch**: `017-feedback-changelog-health-probes` | **Date**: 2026-07-29 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/017-feedback-changelog-health-probes/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

This is **Layer 1** of [the sequencing plan](../../docs/spec-sequencing-plan.md) — depends only on spec 002 (session/role), already implemented. Per [`docs/Release1-MVP Plan.md`](../../docs/Release1-MVP%20Plan.md): "017 — minimal: liveness/readiness probes + changelog surface. Feedback capture optional-but-recommended for pilot signal."

**R1 scope: US1, US2, US3, US4 (all in scope)**. **Deferred: US5** (real-time long-running-operation notifications, FR-014–016) — no long-running operation exists anywhere in R1 to notify about (ingestion is spec 005, bulk-delete isn't built); building a SignalR/Web PubSub channel now would have no caller, the same "no R1 caller yet" reasoning used to defer spec 014's remaining provider adapters and spec 004's artifacts.

**Why US3/US4 are in scope despite being P3**: the MVP doc calls feedback "optional-but-recommended for pilot signal," and US4 (version-alert acknowledgment) is the other half of the same "changelog surface" the MVP doc explicitly asks for — US1 alone (graceful degradation) has nothing to degrade gracefully *from* without also building the normal-case changelog read + version comparison, which is US4's mechanism. Building both together, fully, is cheaper than half-building a feature.

**Health checks are a real upgrade, not a new endpoint**: spec 002 already declared `/health/live` and `/health/ready` as public routes, but they're static stubs (`Results.Ok(new { status = "live" })`) that check nothing. This spec replaces their implementation with real dependency checks (Cosmos DB, Azure Key Vault) using ASP.NET Core's built-in `Microsoft.Extensions.Diagnostics.HealthChecks` — same routes, same public-route contract, now actually meaningful.

**Key R1 scoping decision — dependency-unconfigured is "Healthy," not "Unhealthy"**: mirrors every prior spec's "boots without a live dependency in dev/test" convention (Cosmos in 002, SQL/Redis in 014, Content Safety's dev-bypass in 004). If `Cosmos:AccountEndpoint` or `KeyVault:VaultUri` is unset, that check reports Healthy with a "not configured" note rather than attempting a doomed connection — this keeps the existing spec 002 `RouteAuthorizationTests.PublicHealthRoute_Anonymous_IsServed` test (and every test factory, none of which configure live Cosmos/Key Vault) passing without modification. `KeyVaultOptions` gets the same Production-fails-startup / Development-allowed validation spec 004 established for `ContentSafetyOptions`, so this convenience never silently ships to a real deployment.

## Technical Context

**Language/Version**: C# on **.NET 10** — same solution as specs 002/004/006/014; new project folders, not new projects.

**Primary Dependencies**: `Microsoft.Extensions.Diagnostics.HealthChecks` (part of the ASP.NET Core shared framework, no new package) for US2; new package `Azure.Security.KeyVault.Secrets` (workload-identity-authenticated, first real use of spec 002's previously-unused Key Vault mention in the constitution) for the Key Vault health check. No new package for US1/US4 (file-system changelog source, Cosmos for acknowledgment persistence) or US3 (plain `HttpClient` to the external ECPI API).

**Storage**: **Azure Cosmos DB** — `VersionAcknowledgmentModel` uses spec 002's `users` container (declared in `CosmosOptions` since spec 002 but never actually used by any concrete store until now), partition-keyed by the same hashed identity. Changelog content is file-system-sourced (a configured content directory), not database-backed — matches spec.md's Assumptions ("whatever content store... backs the changelog reader"). Feedback is explicitly never persisted (FR-009) — forwarded only.

**Testing**: **xUnit**; unit tests for the changelog reader's missing/malformed-source fallback (SC-001), the alert-visibility window calculation (SC-005), and the health-check sanitization (no raw exception text in the response, SC-002); `WebApplicationFactory` integration tests for `/health/live`/`/health/ready` (healthy-when-unconfigured, unhealthy-when-configured-but-unreachable), the feedback ownership rejection (SC-003), and the acknowledgment endpoint. No live Cosmos/Key Vault/ECPI dependency in CI — same fake-substitution pattern as every prior spec.

**Target Platform**: Same as specs 002/004/006/014 — Linux containers on Azure, fronted by Entra ID.

**Project Type**: Web application — extends the existing layered monolith. `/changelog` and version-alert are ultimately UI-facing, but per specs 002/004/006/014's precedent, this pass ships the API/service layer only; the Blazor pages are a follow-up task.

**Performance Goals**: Each health check respects a 5-second timeout (FR-006), evaluated in parallel via `HealthCheckRegistration.Timeout` — the framework's built-in mechanism, not custom code.

**Constraints**: Health responses never include `Exception`/raw provider error text (FR-005) — enforced by a custom response writer that serializes only check name + status, never `HealthReportEntry.Exception`/`Description`. Feedback ownership is verified before any external call (FR-008) — reuses spec 004's `IChatThreadStore.GetAsync`, not a new authorization mechanism (spec.md Assumptions).

**Scale/Scope**: Internal enterprise pilot. R1 tasks US1–US4. FR-001–FR-013; SC-001–SC-005.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluated against constitution v1.2.1.

| Principle / Constraint | Assessment | Verdict |
|---|---|---|
| **I. Azure-Only, No AWS Vestiges** | Key Vault via workload identity; no AWS-shaped code. US5 (deferred) explicitly assumed Azure-native (Web PubSub/SignalR) per spec.md Assumptions when it's eventually built — not this pass's concern. | ✅ PASS |
| **II. Explicit, Server-Side Authorization** | Feedback's thread-ownership check runs server-side before any external call (FR-008), reusing spec 004's existing `IChatThreadStore` — no new, parallel authorization path. | ✅ PASS |
| **III. Fail Loud, Never Fabricate Success** | Health checks report real Unhealthy status on a genuine connectivity failure — the "unconfigured = Healthy" convenience is a documented, explicit dev/test-only carve-out (mirroring specs 002/004/014), not a silent fabrication of production health; `KeyVaultOptions` fails startup in Production if unconfigured, so this can't silently reach a real deployment. | ✅ PASS |
| **IV. One Implementation Per Concern** | One `IChangelogReader`, one `IVersionAcknowledgmentStore`, one `IFeedbackForwarder`, one health-check-per-dependency — architecture-tested. | ✅ PASS |
| **V. Schema-Enforced, Not UI-Enforced, Validation** | Feedback ownership and the alert-window calculation live in the Application-layer service, not a client check — there's no UI yet to bypass. | ✅ PASS |
| **VI. Testable, EARS-Style Requirements** | Spec FRs are EARS/`MUST`; each R1-scoped SC maps to a concrete test. | ✅ PASS |
| **Tech Stack Alignment** | Azure Key Vault (constitution-named, first real use), Cosmos DB for the `users` container (constitution-named for user-scoped storage) — no new stack element introduced. | ✅ PASS |
| **Security & Compliance Constraints** | Health responses exclude raw error/connection detail structurally (custom response writer, not per-call-site discipline); the ECPI API key (a genuine third-party, non-Azure credential — distinct from the Azure-workload-identity cases elsewhere) is read from configuration, never hardcoded, and excluded from any client-facing accessor. | ✅ PASS |

**Result**: No violations. Complexity Tracking is intentionally empty. Deferring US5 is a scope decision (Summary), not a constitution exception.

## Project Structure

### Documentation (this feature)

```text
specs/017-feedback-changelog-health-probes/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md         # Phase 1 output (/speckit-plan command)
├── contracts/            # Phase 1 output (/speckit-plan command)
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

Extends the existing layered solution (specs 002/004/006/014) — no new projects.

```text
EnterpriseAIPlatform.sln
src/
├── EnterpriseAIPlatform.Web/
│   └── Endpoints/Support/                  # GET /api/changelog, GET/POST /api/changelog/acknowledgment, POST /api/feedback
├── EnterpriseAIPlatform.Application/
│   └── Support/                            # IChangelogReader, IVersionAcknowledgmentStore, IFeedbackForwarder contracts;
│                                            # AlertWindowEvaluator (pure function, mirrors ModelAccessEvaluator/MultiChatQuadrantRules)
├── EnterpriseAIPlatform.Infrastructure/
│   ├── Support/                             # FileSystemChangelogReader, CosmosVersionAcknowledgmentStore, EcpiFeedbackForwarder
│   └── HealthChecks/                        # CosmosHealthCheck, KeyVaultHealthCheck, sanitizing response writer
└── EnterpriseAIPlatform.Domain/
    └── Support/                             # ChangelogEntry, VersionAcknowledgmentModel, FeedbackSubmission value objects
tests/
├── EnterpriseAIPlatform.UnitTests/          # changelog missing/malformed fallback, alert-window evaluator, health-check sanitization
├── EnterpriseAIPlatform.IntegrationTests/   # health endpoints (configured/unconfigured), feedback ownership rejection, acknowledgment endpoint
└── EnterpriseAIPlatform.ArchitectureTests/  # single implementation per contract
```

**Structure Decision**: `Support` is a new peer feature-module folder to `Chat`/`ModelAccess` — this spec's four stories (changelog, health, feedback, version-alert) are a real domain grouping (support/operability features), matching spec.md's own framing ("Domain: Support Features"), not an arbitrary bundling.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No constitution violations — this section is intentionally empty.

## Phase 0 & 1 Artifacts

- [research.md](./research.md) — technology decisions (Phase 0)
- [data-model.md](./data-model.md) — entities & validation (Phase 1)
- [contracts/](./contracts/) — service interfaces, route table (Phase 1)
- [quickstart.md](./quickstart.md) — end-to-end validation guide (Phase 1)
