# Tasks: Feedback, Changelog & Health Probes (R1 subset)

**Input**: Design documents from `/specs/017-feedback-changelog-health-probes/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)
**Tests**: Included — this spec's R1-scoped success criteria (SC-001–SC-005) are defined as test suites, and Constitution Principle VI requires falsifiable per-story tests.

**Release 1 scope note** (per `plan.md`'s Summary): this tasks.md covers **US1, US2, US3, US4 (all in scope)**. **Explicitly deferred to R2 — no tasks generated here, not forgotten**:
- **US5** (real-time long-running-operation notifications, FR-014–016) — no long-running operation exists anywhere in R1 to notify about (ingestion is spec 005, bulk-delete isn't built); no caller yet, same reasoning used to defer spec 014's remaining provider adapters and spec 004's artifacts.

## Implementation status (2026-07-29)

All 24 R1 tasks complete. `dotnet build` clean; **158/158 tests pass** across the whole solution (18 architecture, 95 unit, 45 integration — up from specs 002+004+006+014's 138, +20 for this spec). No live Cosmos/Key Vault/ECPI dependency required.

The existing spec 002 `/health/live`/`/health/ready` stub endpoints were replaced (same routes, same public-route contract) with real `Microsoft.Extensions.Diagnostics.HealthChecks`-backed checks — `RouteAuthorizationTests.PublicHealthRoute_Anonymous_IsServed` (spec 002) stayed green throughout because unconfigured Cosmos/Key Vault report Healthy (D6), matching every existing test factory's defaults.

A minor test-scope issue surfaced and was fixed: spec 014's `AzureFoundryProviderAdapterTests.ClientDeliveredConfig_ContainsNoLongLivedProviderSecret` did a blanket string-scan of the whole `appsettings.json` for "apikey," which false-positived on this spec's unrelated `Feedback:EcpiApiKey` (a legitimate third-party, non-Azure credential placeholder). Narrowed that test to scan only the `ModelProviders` config section, matching its actual intent (spec 014 FR-013).

## Path Conventions (from plan.md — extends specs 002/004's layered solution, no new project)

- `src/EnterpriseAIPlatform.Domain/Support/`, `.Application/Support/`, `.Infrastructure/{Support,HealthChecks}/`, `.Web/Endpoints/Support/`
- `tests/EnterpriseAIPlatform.UnitTests/`, `.IntegrationTests/`, `.ArchitectureTests/`

---

## Phase 1: Setup

- [X] T001 Add `Azure.Security.KeyVault.Secrets` NuGet dependency to `EnterpriseAIPlatform.Infrastructure`
- [X] T002 [P] Add `Changelog:ContentDirectory`, `KeyVault:VaultUri`, `Feedback:EcpiApiEndpoint`, `Feedback:EcpiApiKey` placeholders (no secrets) to `appsettings.json` in `src/EnterpriseAIPlatform.Web/`

**Checkpoint**: Solution still builds with the new dependency restored.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ No user story work begins until this phase is complete.**

- [X] T003 [P] Create `ChangelogEntry`, `VersionAcknowledgmentModel`, `FeedbackSubmission` types in `src/EnterpriseAIPlatform.Domain/Support/`
- [X] T004 [P] Declare `IChangelogReader`, `IVersionAcknowledgmentStore`, `IFeedbackForwarder` contracts and the `AlertWindowEvaluator` pure function in `src/EnterpriseAIPlatform.Application/Support/`
- [X] T005 Implement `KeyVaultOptions` with startup validation (fails app boot if unconfigured in Production; allowed in Development), mirroring spec 004's `ContentSafetyOptions`, in `src/EnterpriseAIPlatform.Infrastructure/HealthChecks/`
- [X] T006 Create `AddSupportInfrastructure` DI registration extension in `src/EnterpriseAIPlatform.Infrastructure/DependencyInjection/`

**Checkpoint**: Contracts compile; `KeyVaultOptions` validation is unit-testable without a live Key Vault.

---

## Phase 3: User Story 1 — Changelog and version-alert degrade gracefully (Priority: P1) 🎯 MVP

**Goal**: `/changelog` and the version-alert lookup never throw when the changelog source is missing or malformed.
**Independent Test**: With no changelog source content present, call the changelog read path directly and confirm an empty result, never an exception (SC-001).

- [X] T007 [P] [US1] Unit tests: `FileSystemChangelogReader` returns an empty list for a missing directory and for a directory containing an unparseable filename; returns correctly-ordered entries for a well-formed directory (SC-001) in `tests/EnterpriseAIPlatform.UnitTests/`
- [X] T008 [US1] Implement `FileSystemChangelogReader` in `src/EnterpriseAIPlatform.Infrastructure/Support/`
- [X] T009 [US1] Implement `GET /api/changelog` in `src/EnterpriseAIPlatform.Web/Endpoints/Support/`

**Checkpoint**: US1 fully functional and independently testable (MVP).

---

## Phase 4: User Story 2 — Health probes report degraded status without leaking internal error detail (Priority: P2)

**Goal**: `/health/live`/`/health/ready` report real dependency status, identify a failing dependency by name, and never include raw error/connection detail.
**Independent Test**: Force a dependency into a failing state and confirm the response signals unhealthy with no raw error text (SC-002); confirm unconfigured dependencies report Healthy (no regression on the existing spec 002 test).

- [X] T010 [P] [US2] Unit tests: `CosmosHealthCheck`/`KeyVaultHealthCheck` return Healthy immediately when unconfigured; return Unhealthy (with no exception/connection text in the result) when configured against an unreachable target in `tests/EnterpriseAIPlatform.UnitTests/`
- [X] T011 [P] [US2] Integration test: `/health/live` and `/health/ready` return `200 Healthy` when Cosmos/Key Vault are unconfigured (matches every existing test factory, SC-002 adjacent); a fake failing check maps to a sanitized Unhealthy body identifying the check by name in `tests/EnterpriseAIPlatform.IntegrationTests/`
- [X] T012 [US2] Implement `CosmosHealthCheck` and `KeyVaultHealthCheck` (`IHealthCheck`, unconfigured-is-Healthy per D6) in `src/EnterpriseAIPlatform.Infrastructure/HealthChecks/`
- [X] T013 [US2] Register both checks (Cosmos tagged `"live"`+`"ready"`, Key Vault tagged `"ready"` only, `Timeout = 5s`) and a sanitizing response writer; replace the `/health/live`/`/health/ready` stub `MapGet` calls with `MapHealthChecks` in `src/EnterpriseAIPlatform.Web/Program.cs`

**Checkpoint**: US2 independently testable; existing spec 002 health-route test still green.

---

## Phase 5: User Story 3 — Feedback stays a pure, ownership-checked proxy (Priority: P3, R1 backend contract)

**Goal**: Feedback is rejected before any external call when the caller doesn't own the referenced thread; forwarding failures are logged only, never surfaced.
**Independent Test**: Submit feedback for a thread the caller doesn't own and confirm rejection before forwarding (SC-003); submit valid feedback with a simulated ECPI outage and confirm the caller sees success while the failure is logged (SC-004).

- [X] T014 [P] [US3] Integration test: feedback referencing a thread the caller doesn't own (or that doesn't exist) is rejected before any forwarder call (SC-003) in `tests/EnterpriseAIPlatform.IntegrationTests/`
- [X] T015 [P] [US3] Integration test: valid feedback for the caller's own thread, with a fake `IFeedbackForwarder` simulating failure, still returns success to the caller (SC-004) in `tests/EnterpriseAIPlatform.IntegrationTests/`
- [X] T016 [US3] Implement `IFeedbackForwarder`/`EcpiFeedbackForwarder` (catches and logs, never throws to the caller) in `src/EnterpriseAIPlatform.Infrastructure/Support/`
- [X] T017 [US3] Implement `POST /api/feedback` (ownership check via spec 004's `IChatThreadStore.GetAsync`, then forward; always success to the caller once ownership passes) in `src/EnterpriseAIPlatform.Web/Endpoints/Support/`

**Checkpoint**: US3 independently testable, reusing spec 004's thread store directly.

---

## Phase 6: User Story 4 — Version-alert acknowledgment respects the window and never leaves a false state (Priority: P3)

**Goal**: The alert shows/hides correctly around the 60-day cooldown; a failed persistence write is legible to the (future) client as a non-success, never a fabricated success.
**Independent Test**: Set the last acknowledgment just inside and just outside 60 days and confirm alert visibility flips accordingly (SC-005).

- [X] T018 [P] [US4] Unit tests: `AlertWindowEvaluator` — no acknowledgment shows the alert; an acknowledgment under 60 days old suppresses it; one over 60 days old shows it again (SC-005) in `tests/EnterpriseAIPlatform.UnitTests/`
- [X] T019 [P] [US4] Integration test: `POST /api/changelog/acknowledgment` then `GET` returns the persisted value; a simulated store failure (fake) returns a non-2xx rather than a fabricated success in `tests/EnterpriseAIPlatform.IntegrationTests/`
- [X] T020 [US4] Implement `CosmosVersionAcknowledgmentStore` (spec 002's `users` container) in `src/EnterpriseAIPlatform.Infrastructure/Support/`
- [X] T021 [US4] Implement `GET`/`POST /api/changelog/acknowledgment` in `src/EnterpriseAIPlatform.Web/Endpoints/Support/`

**Checkpoint**: All R1-scoped stories (US1–US4) independently functional.

---

## Phase 7: Polish & Cross-Cutting

- [X] T022 [P] Architecture test asserting exactly one implementation each of `IChangelogReader`, `IVersionAcknowledgmentStore`, `IFeedbackForwarder`, and that `AlertWindowEvaluator` is the single static implementation in `tests/EnterpriseAIPlatform.ArchitectureTests/`
- [X] T023 [P] Update the solution README with spec 017's endpoints and the explicit R1 (US1–US4) vs. R2 (US5) scope note
- [X] T024 Verify SC-001–SC-005 are each covered by a passing test; confirm SC-006/SC-007 (US5) are explicitly recorded as deferred to R2, not silently skipped

**SC coverage map (verified 2026-07-29, 158/158 tests passing across the solution):**
| SC | Covered by |
|---|---|
| SC-001 | `FileSystemChangelogReaderTests` (missing/malformed/well-formed source) |
| SC-002 | `HealthCheckTests` (unconfigured-Healthy); `SupportEndpointsTests.Health_FailingDependency_ReportsUnhealthy_NeverLeaksRawErrorText` |
| SC-003 | `SupportEndpointsTests.Feedback_ThreadNotOwnedByCaller_IsRejected_BeforeAnyForwarding` |
| SC-004 | `SupportEndpointsTests.Feedback_OwnedThread_ForwarderFails_CallerStillSeesSuccess` |
| SC-005 | `AlertWindowEvaluatorTests`; `SupportEndpointsTests.Acknowledgment_*` |
| SC-006 | **Deferred to R2** — no real-time transport or long-running operation exists in R1 (US5) |
| SC-007 | **Deferred to R2** — same as SC-006 |

---

## Dependencies & Execution Order

- **Setup** → **Foundational** block everything.
- **US1** (Phase 3) depends only on Foundational.
- **US2** (Phase 4) depends only on Foundational — independent of US1/US3/US4, can proceed in parallel.
- **US3** (Phase 5) depends on Foundational + spec 004's already-implemented `IChatThreadStore`.
- **US4** (Phase 6) depends on Foundational + US1's `ChangelogEntry`/`IChangelogReader` (the alert needs "latest version" to compare against).
- Polish last.
- **Deferred to R2, not sequenced here**: US5 (depends on a real-time transport and an actual long-running operation to notify about — neither exists in R1).

**Parallel opportunities**: T001/T002; T003/T004; all `[P]` test tasks within a phase; US1, US2, and US3 can all proceed in parallel once Foundational lands (US4 needs US1's reader first).

## Implementation Strategy

Deliver **US1 first as the MVP checkpoint** (the crash-risk fix, and the prerequisite for US4), then **US2** (health probes, independent), then **US3** (feedback, independent), then **US4** (builds on US1). Keep the architecture test (T022) green throughout. Do not begin US5 work against this tasks.md — that requires its own `/speckit-tasks` pass once a real long-running operation exists to notify about.
