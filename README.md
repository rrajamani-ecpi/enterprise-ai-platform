# enterprise-ai-platform

Enterprise AI Platform — a spec-driven (GitHub spec-kit) build. Specifications live under [`specs/`](./specs/); the platform constitution (principles + technology stack) is in [`.specify/memory/constitution.md`](./.specify/memory/constitution.md); the release plan is in [`docs/spec-sequencing-plan.md`](./docs/spec-sequencing-plan.md).

## Release 1 — Authenticated Enterprise Chat (in progress)

The first increment is the **walking skeleton** (spec 002): the canonical server-side session/role/authorization layer every later feature builds on. Spec 014 (Layer 1) adds the model registry, access-gating, and config-management service. Spec 017 (Layer 1) adds changelog/version-alert, sanitized health probes, and a feedback proxy. Spec 004 (Layer 3) adds the first user-facing capability — the chat send → stream → persist pipeline, hardened with PII redaction, a Content Safety guardrail, and a reliability wrapper. Spec 006 (Layer 4) adds multi-chat: a persisted, multi-quadrant layout that dispatches one message to N models in parallel for side-by-side comparison. All R1 scope only (see Status sections below), per [`docs/Release1-MVP Plan.md`](./docs/Release1-MVP%20Plan.md).

## Tech stack

- .NET 10 (LTS), C#
- Blazor Web App (Interactive Server render mode)
- Microsoft.Identity.Web + Microsoft Entra ID (OIDC)
- Azure Cosmos DB (user-scoped storage; chat threads/messages), Azure SQL Database via EF Core (admin/system config), Azure Cache for Redis, Azure AI Content Safety, Azure Key Vault, workload identity
- `Microsoft.Extensions.Http.Resilience` (timeout/retry/circuit-breaker around model calls)
- OpenTelemetry + Azure Monitor / Application Insights
- Tests: xUnit, NSubstitute, `WebApplicationFactory`, EF Core InMemory

## Solution layout

```text
src/
  EnterpriseAIPlatform.Domain           # role flags, identity value objects; ModelAccess/ entities (014); Chat/ entities (004/006); Support/ entities (017)
  EnterpriseAIPlatform.Application       # contracts + ServerActionResponse, RoleDowngrade, PolicyNames; ModelAccess/ (014); Chat/ (pipeline/redaction/safety/completion-client contracts (004), multi-chat session store contract + quadrant rules (006)); Support/ (changelog/acknowledgment/feedback contracts + alert-window evaluator, 017)
  EnterpriseAIPlatform.Infrastructure    # Entra claims transformation, current-user accessor, role resolver, identity hasher, Cosmos, telemetry; ModelAccess/ (014); Chat/ (pipeline, Cosmos stores, daily counter (004); multi-chat session store + fan-out/fan-in dispatcher (006)); Redaction/ (regex PII), Safety/ (Content Safety), ModelProviders/ (Foundry adapter + chat completion client, 004); Support/ (changelog reader, feedback forwarder, acknowledgment store, 017), HealthChecks/ (Cosmos/Key Vault checks, 017)
  EnterpriseAIPlatform.Web               # Blazor host, authZ policies, health/whoami/admin endpoints; Endpoints/ModelAccess (014); Endpoints/Chat (004); Endpoints/MultiChat (006); Endpoints/Support (017); Endpoints/UserPreferences (004/014)
tests/
  EnterpriseAIPlatform.UnitTests         # downgrade, role mapping, hashing, no-session (002); model-access/catalog/config (014); chat pipeline gate ordering, PII redaction, Content Safety, resilience wiring (004); quadrant floor/cap, fan-in merge (006); changelog reader, alert-window, health-check sanitization (017)
  EnterpriseAIPlatform.IntegrationTests  # route + admin gating (002); admin gate, soft delete, catalog metadata (014); send-message endpoint streaming + persistence, generic-500 (004); session restore, parallel-send, error isolation (006); health probes, feedback ownership, acknowledgment round-trip (017)
  EnterpriseAIPlatform.ArchitectureTests # one-implementation-per-concern guards for all five specs
```

## Build, test, run

```bash
dotnet build
dotnet test
dotnet run --project src/EnterpriseAIPlatform.Web
```

Local runs use the development-only authentication mode configured in
`appsettings.Development.json`. Run `dotnet run --project src/EnterpriseAIPlatform.Web --launch-profile https`
and open `https://localhost:7231`; the app signs in as
`developer@localhost` without an Entra app registration. A yellow banner remains visible while
the simulated identity is active. You can change its canonical role flags under
`PlatformAuthentication:DevelopmentUser` to exercise server-side authorization scenarios.

The mode is guarded twice: configuration defaults to `Entra` outside Development, and startup
fails if `PlatformAuthentication:Mode=Development` is set in any non-Development environment.
To exercise real Entra authentication locally, override the mode:

```bash
PlatformAuthentication__Mode=Entra dotnet run --project src/EnterpriseAIPlatform.Web
```

For repeatable local Entra app-registration provisioning and local client-secret setup, see [`infra/identity/README.md`](./infra/identity/README.md).

### Configuration (no secrets in source)

Set these before running against a real tenant (use user-secrets or environment variables):

- `AzureAd:TenantId`, `AzureAd:ClientId` — Entra app registration (enable the **groups** claim).
- `RoleDerivation:Mappings` — Entra group-GUID → role (`Admin` / `Employee` / `Contractor` / `Student`). Validated at startup.
- `PublicRoutes:Paths` — the explicit anonymous allow-list (health, sign-in, LMS launch/error).
- `Cosmos:AccountEndpoint`, `ApplicationInsights:ConnectionString` — optional; the app boots without them in local dev.
- `ModelAccessSql:ConnectionString` — Azure SQL for the model registry/admin config (spec 014); the app boots without it (EF Core connects lazily, matching `CosmosClientProvider`'s pattern).
- `ModelAccessCache:RedisConnectionString` — optional; falls back to an in-process distributed cache if unset.
- `ModelProviders:AzureFoundry:Endpoint` — the R1 model endpoint. Deliberately has no API-key field: the adapter authenticates via workload identity (`DefaultAzureCredential`), never a static secret (FR-013).
- `Cosmos:ChatContainerName` — holds chat thread/message documents (spec 004), default `chat`.
- `ContentSafety:Endpoint` — the Content Safety guardrail. **Required in Production** (app fails to start if unset); optional in Development (allows messages through with a logged warning) so local dev doesn't need a live resource.
- `Changelog:ContentDirectory` — Markdown changelog source, one file per version (spec 017), default `content/changelog`.
- `KeyVault:VaultUri` — backs the `/health/ready` Key Vault check. **Required in Production**; optional in Development (reports Healthy without a live resource).
- `Feedback:EcpiApiEndpoint`, `Feedback:EcpiApiKey` — the external, non-Azure ECPI Feedback API (spec 017). A static API key is the correct credential shape here — ECPI isn't an Azure resource, so workload identity doesn't apply.

## Status (spec 002)

Implemented + tested: US1 (single-source downgrade), US2 (structured no-session), US4 (role mapping), US5 (route/admin gating), US6 (identity hashing).
Follow-up: US3 (token-refresh → forced re-auth) is satisfied by Microsoft.Identity.Web's challenge-on-expiry today; its full FR-007 behavior lands when downstream token acquisition is added (spec 004).

## Status (spec 014)

**R1 scope implemented + tested** (86/86 tests passing): US1 (admin gate on all mutation endpoints), US2 (effective-access computation + soft delete), US3 (message-limit/persona-generation-model read-open/write-gated asymmetry), US4 (server-side re-validation), US5 (`/api/user/preferences/*` 401 fix), US6 (catalog metadata, single Azure/Foundry provider), US8 (workload-identity provider auth).

**Explicitly deferred to R2** — not started, not forgotten: US7's remaining provider adapters (Claude, Vertex, DeepSeek, Llama, Mistral, Kimi) against the existing `IModelProviderAdapter` seam, and the admin config UI (all R1 mutation endpoints are API-only). SC-008 (multi-provider client-code claim) is untestable until those adapters exist.

A pre-existing walking-skeleton bug surfaced while building 014's admin write endpoints: `UseStatusCodePagesWithReExecute("/not-found", ...)` re-executed *any* non-2xx API response (403/404/etc.) against the Blazor not-found page, and since that page only supports GET/HEAD/POST, PUT/DELETE admin requests were corrupted into a spurious 405. Fixed by scoping that middleware to non-`/api` requests in `Program.cs`.

## Status (spec 004)

**R1 scope implemented + tested** (119/119 tests passing across the solution): US1 in full (message-limit preflight, read-only-thread gate, fail-open, server-authoritative `dataProducts`) and the R1 subset of US5 (model-access substitution via spec 014, generic 500 with no leaked detail) — plus three constitution-mandated additions with no spec-004 FR number: PII redaction (regex-based, the constitution's fail-closed tier), an Azure AI Content Safety guardrail at the model boundary, and a `Microsoft.Extensions.Http.Resilience` wrapper around the model call. This is the first working **send → stream → persist** vertical: `POST /api/chat/threads` and `POST /api/chat/threads/{id}/messages` (streamed as `text/event-stream`), backed by new Cosmos-stored `ChatThreadModel`/`ChatMessageModel` documents.

**Explicitly deferred to R2+** — not started, not forgotten: US2 (React artifact sandboxing) and US9 (artifact panel) — no artifacts in R1; US3 (share-recipient/expiry) — spec 018 not in R1; US4 (consistent tool-failure shape) — spec 020 not in R1; US5's image-size/CSV-injection guards — no multimodal/CSV export in R1; US6 (context compression) and US7 (max-thread-size handling); US8 (feedback capture) — blocked on spec 017; US10 (persona-launch) — specs 009/010 not in R1.

The Blazor chat UI itself is a follow-up task — this phase ships the server-side pipeline and API surface only, consistent with how specs 002/014 shipped API-only.

A second pre-existing bug surfaced here: `StreamWriter { AutoFlush = true }` triggers a **synchronous** `Flush()`, which `TestServer` (and some hosts) disallow on the response body, corrupting every streamed response into a spurious 500. Fixed by flushing explicitly and asynchronously after each chunk.

## Status (spec 006)

**R1 scope implemented + tested** (138/138 tests passing across the solution): US1 (multi-chat layout — quadrant count, model assignment, thread association — durably persisted and restored on load; on-demand thread creation persisted immediately, before the send proceeds), US2 (a thread-creation failure for one quadrant surfaces as a distinct, non-fatal per-quadrant error, never a generic 500 or a silently swallowed failure), and US4 (one message dispatched concurrently to every quadrant's assigned model, merged into one tagged `text/event-stream`, unblocked by another quadrant's latency or failure). Built **as written** per spec.md — flagged and confirmed with the user first, since it doesn't match `docs/Release1-MVP Plan.md`'s shorthand description of "006."

**Key R1 scoping call**: spec.md describes per-quadrant *persona* assignment, but personas (specs 009/010) aren't in R1. The full `MultiChatSession` schema includes a `PersonaId` slot so personas slot in later without a schema change, but only **model** assignment (spec 014) is functional in R1.

**Explicitly deferred to R2** — not started, not forgotten: US3 (Chat-Home starred personas) — no data-loss risk, and blocked on personas anyway.

The quadrant floor/cap invariant (FR-004/005) lives in a pure `MultiChatQuadrantRules` static class (no I/O), shared by the real Cosmos store and the test fake — mirrors spec 014's `ModelAccessEvaluator` pattern so the two can't drift apart. The Blazor multi-chat UI itself is a follow-up task, consistent with specs 002/004/014 shipping API-only.

## Status (spec 017)

**R1 scope implemented + tested** (158/158 tests passing across the solution): US1 (changelog/version-alert lookup never throws on a missing/malformed source — returns an empty list instead), US2 (`/health/live`/`/health/ready` now run real Cosmos DB + Key Vault checks, sanitized so a failure identifies the dependency by name with zero raw error/connection text), US3 (feedback is rejected before any external call when the caller doesn't own the referenced thread; forwarding failures are logged only, never surfaced), and US4 (a 60-day acknowledgment cooldown governs version-alert visibility; a failed persistence write returns a non-2xx rather than a fabricated success).

**Health checks are an upgrade, not a new route**: `/health/live`/`/health/ready` are the same routes spec 002 declared public — they were static stubs checking nothing; this spec wires them to `Microsoft.Extensions.Diagnostics.HealthChecks` for the first time. An unconfigured Cosmos/Key Vault reports **Healthy** (not Unhealthy) — a documented dev/test-only convenience (mirroring specs 002/004/014's "boots without a live dependency" pattern) that kept spec 002's existing health-route test green without modification; `KeyVault:VaultUri` unconfigured in Production still fails app startup, so this can't silently reach a real deployment.

**Explicitly deferred to R2** — not started, not forgotten: US5 (real-time long-running-operation notifications) — no long-running operation exists anywhere in R1 to notify about.

A minor test-scope issue surfaced here: spec 014's provider-secret scan test did a blanket string search across all of `appsettings.json`, which false-positived on this spec's unrelated `Feedback:EcpiApiKey` (a legitimate third-party credential placeholder, not an Azure model-provider secret). Narrowed that test to the `ModelProviders` config section, matching its actual intent.

## Status (spec 018)

**Evaluator-only, implemented + tested** (36 unit tests + 2 architecture tests, all passing): US1–US4 — `ISharingPolicyService`/`SharingPolicyEvaluator` compute the canonical allow/deny `SharingDecision` for a share-target request from the caller's role flags, the per-role `RoleSharing` config, and any active `GlobalSharingOverride` (admin bypass, per-role individual/group rules, disable-all-group-sharing, admin-only mode, globally-allowed-groups precedence, and the `ShareTarget` read-default/explicit-collaborator split). No database, cache, or admin UI — both config sections are static `IOptions`-bound app config, per this spec's own clarification.

**Explicitly deferred** — not started, not forgotten: any actual persona/prompt/data-product sharing behavior (specs 009/012/016 adopting `ISharingPolicyService` instead of their own share-validity checks) is a separate, future refactor outside this spec's scope; `RoleName.Contractor` has no defined sharing policy yet (falls back to a fail-safe restrictive default) pending a future clarification.

An implementation-time layering correction surfaced here: `RoleSharingPolicyOptions`/`GlobalSharingOverrideOptions` are implemented in `EnterpriseAIPlatform.Application.Sharing`, not `.Infrastructure.Sharing` as originally planned — `RoleSharingPolicyOptions` is keyed by `RoleName` (itself in `Application.Authorization`) and consumed directly by the pure `SharingPolicyEvaluator` (also Application), so an Infrastructure location would have created a reverse Application→Infrastructure dependency.
