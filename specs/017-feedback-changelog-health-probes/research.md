# Phase 0 Research: Feedback, Changelog & Health Probes

**Feature**: 017-feedback-changelog-health-probes | **Date**: 2026-07-29

Scoped to the R1 subset per plan.md's Summary (US1–US4). Format: Decision / Rationale / Alternatives considered.

---

## D1. Changelog source & "latest version" (US1, FR-001–003)

- **Decision**: `IChangelogReader.GetEntriesAsync()` reads Markdown files from a configured directory (`Changelog:ContentDirectory`, default `content/changelog`), one file per version (filename = version, e.g. `1.2.0.md`), returning `IReadOnlyList<ChangelogEntry>` ordered newest-first. If the directory doesn't exist, is empty, or a file fails to parse, the method returns an **empty list** — never throws. "Latest version" is simply `entries.FirstOrDefault()`.
- **Rationale**: FR-001 requires no unhandled error on a missing/malformed source; returning an empty list (rather than null or a thrown exception) gives every caller (the `/changelog` page, version-alert) a single, always-safe value to branch on. File-system-backed content matches spec.md's Assumption that "whatever content store... backs the changelog reader" is implementation-defined — Markdown files are the simplest content format that doesn't require a database migration for what's fundamentally static release-note text.
- **Alternatives considered**: A Cosmos-backed changelog store (over-engineered for static release notes that ship with the build, not user-generated content); throwing a typed `ChangelogUnavailableException` for callers to catch (reintroduces the exact "throws on missing source" pattern FR-001 exists to eliminate).

## D2. Version comparison and ordering

- **Decision**: Filenames are parsed as `System.Version` (`major.minor.patch`); entries failing to parse are skipped (treated as malformed, per FR-001's "malformed" case) rather than aborting the whole read.
- **Rationale**: `System.Version` gives correct semantic ordering (`1.10.0 > 1.9.0`) without a third-party SemVer package for a need this simple; skipping unparseable files (rather than failing the whole directory read) means one bad file can't take down the rest of the changelog, consistent with the general "one failure shouldn't cascade" theme across this codebase (spec 006's per-quadrant error isolation, spec 004's per-tool failure isolation upstream).
- **Alternatives considered**: A `Version` field inside YAML front-matter instead of the filename (adds a parsing step for no real benefit — the filename is already a natural, greppable version marker); a full SemVer library (unnecessary weight for three-part version numbers).

## D3. Version-alert visibility window (US4, FR-011/012)

- **Decision**: `AlertWindowEvaluator.ShouldShowAlert(ChangelogEntry? latest, VersionAcknowledgmentModel? acknowledgment, DateTimeOffset now)` — a pure function (no I/O) returning true when `latest is not null && (acknowledgment is null || now - acknowledgment.AcknowledgedAtUtc > TimeSpan.FromDays(60))`. The 60-day window is a **cooldown since the last acknowledgment of any version**, not a per-version "have you seen this one" flag — this reading directly satisfies spec.md's own Acceptance Scenario 2 ("acknowledged within the last 60 days" suppresses the alert, without qualifying "acknowledged *this* version").
- **Rationale**: Framing it as a snooze/cooldown (rather than per-version tracking) is the simplest model that satisfies all four of US4's acceptance scenarios without inventing an unstated "did they see version X specifically" requirement the spec never actually asks for. Extracting it as a pure function (mirroring spec 014's `ModelAccessEvaluator`, spec 006's `MultiChatQuadrantRules`) makes the window math fully unit-testable without a database.
- **Alternatives considered**: Per-version acknowledgment tracking (`AcknowledgedVersion == latest.Version`) — plausible, but scenario 2's wording ("acknowledged within the last 60 days," not "acknowledged this version") doesn't require it, and it would mean every patch release re-triggers the alert even one day after a user acknowledged the previous version, which reads against the "cooldown" framing the spec's 60-day language implies.

## D4. Optimistic acknowledgment + revert — server/client boundary (FR-012)

- **Decision**: R1 ships `POST /api/changelog/acknowledgment`, which persists synchronously and returns success/failure — the optimistic-UI-then-revert-on-failure *behavior* is a client-state concern (same boundary spec 006 drew for US2's retry UX) not built here, since no Blazor version-alert component exists yet.
- **Rationale**: The backend's job is to make failure legible (a failed write returns a clear non-2xx, never a fabricated success) so a future client can implement optimistic-then-revert correctly; the backend doesn't need to simulate "optimism" itself.
- **Alternatives considered**: Building a two-phase (tentative/confirm) server API to support optimism server-side (unnecessary — HTTP's own success/failure semantics are sufficient for a client to implement optimistic UI against).

## D5. Health checks (US2, FR-004–007)

- **Decision**: Use ASP.NET Core's built-in `Microsoft.Extensions.Diagnostics.HealthChecks` (already part of the shared framework — no new package) rather than hand-rolled endpoints. `CosmosHealthCheck` and `KeyVaultHealthCheck` implement `IHealthCheck`; each registration sets `Timeout = TimeSpan.FromSeconds(5)` (FR-006's per-check timeout, enforced by the framework, not custom code). Liveness (`/health/live`) filters to the `"live"` tag (Cosmos only, FR-007); readiness (`/health/ready`) filters to `"ready"` (both, FR-006). A custom `IHealthCheckResponseWriter`-equivalent (a `ResponseWriter` delegate on `HealthCheckOptions`) serializes `{ status, checks: [{ name, status }] }` — deliberately omitting `HealthReportEntry.Exception`/`Description`, which is exactly where raw provider error text would otherwise leak (FR-005).
- **Rationale**: This is the idiomatic, already-in-the-framework mechanism for exactly this need — building a custom health aggregator would duplicate what `Microsoft.Extensions.Diagnostics.HealthChecks` already does correctly (parallel execution, per-check timeout, tag-based endpoint filtering).
- **Alternatives considered**: A hand-rolled `IHealthCheckService` (reinvents the framework); including `HealthReportEntry.Description` in the response for "helpfulness" (directly violates FR-005 — `Description` is exactly the field a naive implementation would leak raw exception text through).

## D6. Dependency-unconfigured behavior (health checks in dev/test)

- **Decision**: `CosmosHealthCheck` returns `HealthCheckResult.Healthy("not configured (dev)")` immediately, without attempting a connection, when `CosmosOptions.AccountEndpoint` is empty — same for `KeyVaultHealthCheck` when `KeyVaultOptions.VaultUri` is empty. `KeyVaultOptions` gets the same startup validation spec 004 built for `ContentSafetyOptions`: unconfigured + Production fails app boot; unconfigured + Development is allowed.
- **Rationale**: Every existing test factory (spec 002's `CustomWebApplicationFactory`, spec 004's `ChatWebApplicationFactory`, spec 006's `MultiChatWebApplicationFactory`) leaves `Cosmos:AccountEndpoint` unset — if the Cosmos health check attempted a real connection there, `/health/live`/`/health/ready` would report Unhealthy and break the *existing* spec 002 test (`RouteAuthorizationTests.PublicHealthRoute_Anonymous_IsServed`, which expects 200). Treating "unconfigured" as an explicit, named Healthy state (not a silent skip, not a fabricated pass on a real failure) keeps that test green while still reporting a genuine Unhealthy status the moment a *configured* endpoint is actually unreachable — the dev convenience never reaches Production because of the startup-validation half of this decision.
- **Alternatives considered**: Reporting Unhealthy when unconfigured (breaks every existing test factory and blocks local dev from ever seeing a healthy readiness probe); silently reporting Healthy even when configured-but-unreachable (a direct Principle III violation — fabricating success over a real failure).

## D7. Feedback proxy (US3, FR-008–010)

- **Decision**: `POST /api/feedback` verifies the referenced thread's ownership via spec 004's existing `IChatThreadStore.GetAsync(threadId, callerPartitionKey)` (not found or wrong partition ⇒ reject before any external call, FR-008) then calls `IFeedbackForwarder.ForwardAsync`, whose sole R1 implementation, `EcpiFeedbackForwarder`, POSTs to a configured `Feedback:EcpiApiEndpoint` using a configured `Feedback:EcpiApiKey` header. Any failure (network, non-2xx, misconfiguration) is caught, logged, and never surfaced to the caller (FR-010) — the endpoint always returns success to the user once ownership passes, matching "the user sees no error" exactly.
- **Rationale**: Reuses spec 004's thread store directly rather than re-deriving ownership logic (Principle IV). The ECPI API is an external, non-Azure, third-party system — a static API key read from configuration (never hardcoded, excluded from any client-facing type) is the correct credential shape here, distinct from the Azure-workload-identity pattern used for Azure resources elsewhere in this codebase; spec.md's Assumptions explicitly say ECPI's own auth mechanism is unchanged/out of scope, so this is a reasonable, minimal, configuration-driven implementation of "however ECPI expects to be called."
- **Alternatives considered**: Persisting feedback locally as a fallback when ECPI is unreachable (directly contradicts FR-009 — "MUST NOT be persisted in this application's own data store," no carve-out for the failure case); surfacing a "your feedback couldn't be sent" error to the user (directly contradicts FR-010 and the story's explicit "never disrupt the learning experience" rationale).

## D8. Testing strategy (SC-001–SC-005 — R1 subset)

- **Decision**: xUnit unit tests for `FileSystemChangelogReader`'s missing/malformed-directory fallback (D1), `AlertWindowEvaluator`'s window math (D3), and each health check's sanitization/unconfigured-Healthy behavior (D5/D6, using a fake/absent configuration — no live Cosmos/Key Vault); `WebApplicationFactory` integration tests for `/health/live`/`/health/ready` (both the unconfigured-Healthy and configured-but-unreachable-Unhealthy cases, the latter via a fake health check dependency), the feedback endpoint's ownership rejection (reusing spec 004's `FakeChatThreadStore`), and the acknowledgment endpoint's persist/read round-trip (an in-memory fake `IVersionAcknowledgmentStore`, same pattern as spec 006's `FakeMultiChatSessionStore`).
- **Rationale**: Directly maps to the R1-scoped success criteria; no new live-dependency surface, reusing every prior spec's exact fake-substitution conventions.
- **Alternatives considered**: A live ECPI sandbox/mock server for US3 (unnecessary — `IFeedbackForwarder` is the seam; testing the endpoint's ownership-gate and swallow-and-log behavior against a fake forwarder is sufficient and mirrors D5/D9's precedent from spec 004's `IChatCompletionClient`).

---

**All NEEDS CLARIFICATION resolved for the R1 scope.** US5 (real-time notifications) is a scope decision recorded in plan.md's Summary, not an open unknown blocking Phase 1.
