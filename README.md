# enterprise-ai-platform

Enterprise AI Platform — a spec-driven (GitHub spec-kit) build. Specifications live under [`specs/`](./specs/); the platform constitution (principles + technology stack) is in [`.specify/memory/constitution.md`](./.specify/memory/constitution.md); the release plan is in [`docs/spec-sequencing-plan.md`](./docs/spec-sequencing-plan.md).

## Release 1 — Authenticated Enterprise Chat (in progress)

The first increment is the **walking skeleton** (spec 002): the canonical server-side session/role/authorization layer every later feature builds on. Spec 014 (Layer 1) adds the model registry, access-gating, and config-management service. Spec 004 (Layer 3) adds the first user-facing capability — the chat send → stream → persist pipeline, hardened with PII redaction, a Content Safety guardrail, and a reliability wrapper — all R1 scope only (see Status sections below), per [`docs/Release1-MVP Plan.md`](./docs/Release1-MVP%20Plan.md).

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
  EnterpriseAIPlatform.Domain           # role flags, identity value objects; ModelAccess/ entities (014); Chat/ entities (004)
  EnterpriseAIPlatform.Application       # contracts + ServerActionResponse, RoleDowngrade, PolicyNames; ModelAccess/ (014); Chat/ (pipeline/redaction/safety/completion-client contracts, 004)
  EnterpriseAIPlatform.Infrastructure    # Entra claims transformation, current-user accessor, role resolver, identity hasher, Cosmos, telemetry; ModelAccess/ (014); Chat/ (pipeline, Cosmos stores, daily counter), Redaction/ (regex PII), Safety/ (Content Safety), ModelProviders/ (Foundry adapter + chat completion client, 004)
  EnterpriseAIPlatform.Web               # Blazor host, authZ policies, health/whoami/admin endpoints; Endpoints/ModelAccess (014); Endpoints/Chat, Endpoints/UserPreferences (004/014)
tests/
  EnterpriseAIPlatform.UnitTests         # downgrade, role mapping, hashing, no-session (002); model-access/catalog/config (014); chat pipeline gate ordering, PII redaction, Content Safety, resilience wiring (004)
  EnterpriseAIPlatform.IntegrationTests  # route + admin gating (002); admin gate, soft delete, catalog metadata (014); send-message endpoint streaming + persistence, generic-500 (004)
  EnterpriseAIPlatform.ArchitectureTests # one-implementation-per-concern guards for all three specs
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
