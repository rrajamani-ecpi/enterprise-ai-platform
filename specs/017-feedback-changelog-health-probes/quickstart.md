# Quickstart: Validating Feedback, Changelog & Health Probes (R1 subset)

**Feature**: 017-feedback-changelog-health-probes | **Date**: 2026-07-29

Validation guide for the R1-scoped subset (US1–US4). US5 (real-time notifications) is R2 — not exercised here.

## Prerequisites

- Spec 002's walking skeleton running (session/role) and, for feedback, spec 004's chat pipeline (thread ownership).
- No live Cosmos DB, Key Vault, or ECPI dependency is required for the automated test suite — all three are swapped for fakes/unconfigured-Healthy behavior, matching every prior spec's testing posture.

## Setup

```bash
dotnet restore
dotnet build EnterpriseAIPlatform.sln
dotnet run --project src/EnterpriseAIPlatform.Web
```

## Validation scenarios (one per in-scope story/requirement)

| Story/FR | Steps | Expected (pass) |
|---|---|---|
| **US1 · Missing changelog source** (FR-001/002) | Point `Changelog:ContentDirectory` at a nonexistent path; call `GET /api/changelog`. | `200` with an empty list — never a 500 (SC-001). |
| **US1 · Version-alert lookup on missing source** (FR-003) | With the same missing directory, call `GET /api/changelog/acknowledgment`. | No alert indicated (nothing to compare against) — never a thrown error. |
| **US2 · Health, unconfigured** (FR-004–007) | With no `Cosmos:AccountEndpoint`/`KeyVault:VaultUri` set (the default), call `/health/live` and `/health/ready`. | Both `200 Healthy` — "not configured (dev)" is a valid Healthy state (D6). |
| **US2 · Health, configured but unreachable** | Configure a real-looking but unreachable Cosmos endpoint; call `/health/ready`. | `503`-mapped `Unhealthy`, body identifies `"cosmos"` by name with no raw connection/exception text (SC-002). |
| **US3 · Feedback ownership** (FR-008) | Submit `POST /api/feedback` referencing a thread that doesn't belong to the caller (or doesn't exist). | Rejected before any call to the external ECPI forwarder (SC-003). |
| **US3 · Feedback swallow-on-failure** (FR-010) | Submit valid feedback for the caller's own thread while the (fake) forwarder simulates an ECPI outage. | The caller still receives success; the failure is only logged (SC-004). |
| **US4 · Alert window** (FR-011) | Set a persisted acknowledgment to 61 days ago, then to 59 days ago; call `GET /api/changelog/acknowledgment` after each. | Alert shown at 61 days, not shown at 59 days (SC-005). |
| **US4 · Acknowledge and persist** (FR-012) | `POST /api/changelog/acknowledgment`; call `GET` again immediately. | The new acknowledgment is returned; a simulated persistence failure returns a non-2xx (for the client to revert its optimistic UI against, per D4). |

**Deferred to R2** (not exercised by this quickstart): US5 (real-time long-running-operation notifications, FR-014–016).

## Automated test commands

```bash
dotnet test tests/EnterpriseAIPlatform.UnitTests          # changelog fallback, alert-window math, health-check sanitization
dotnet test tests/EnterpriseAIPlatform.IntegrationTests   # health endpoints, feedback ownership, acknowledgment round-trip
dotnet test tests/EnterpriseAIPlatform.ArchitectureTests  # single implementation per contract
```

## Done when

- [ ] All eight R1-scoped scenarios above pass.
- [ ] All three test projects green, covering SC-001–SC-005.
- [ ] Architecture test confirms exactly one implementation each of `IChangelogReader`, `IVersionAcknowledgmentStore`, `IFeedbackForwarder` (Principle IV).
- [ ] SC-006/SC-007 (US5) are explicitly recorded as deferred to R2, not silently skipped.
