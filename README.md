# enterprise-ai-platform

Enterprise AI Platform — a spec-driven (GitHub spec-kit) build. Specifications live under [`specs/`](./specs/); the platform constitution (principles + technology stack) is in [`.specify/memory/constitution.md`](./.specify/memory/constitution.md); the release plan is in [`docs/spec-sequencing-plan.md`](./docs/spec-sequencing-plan.md).

## Release 1 — Authenticated Enterprise Chat (in progress)

The first increment is the **walking skeleton** (spec 002): the canonical server-side session/role/authorization layer every later feature builds on. Spec 014 (Layer 1) adds the model registry, access-gating, and config-management service on top of it — R1 scope only (see [Status](#status-spec-014) below); 014 → 004 (chat pipeline) is next per [`docs/Release1-MVP Plan.md`](./docs/Release1-MVP%20Plan.md).

## Tech stack

- .NET 10 (LTS), C#
- Blazor Web App (Interactive Server render mode)
- Microsoft.Identity.Web + Microsoft Entra ID (OIDC)
- Azure Cosmos DB (user-scoped storage), Azure SQL Database via EF Core (admin/system config), Azure Cache for Redis, Azure Key Vault, workload identity
- OpenTelemetry + Azure Monitor / Application Insights
- Tests: xUnit, NSubstitute, `WebApplicationFactory`, EF Core InMemory

## Solution layout

```text
src/
  EnterpriseAIPlatform.Domain           # role flags, identity value objects; ModelAccess/ entities (spec 014)
  EnterpriseAIPlatform.Application       # contracts + ServerActionResponse, RoleDowngrade, PolicyNames; ModelAccess/ (effective-access evaluator, service interfaces, provider-adapter contract)
  EnterpriseAIPlatform.Infrastructure    # Entra claims transformation, current-user accessor, role resolver, identity hasher, Cosmos, telemetry; ModelAccess/ (EF Core DbContext, Redis cache-with-fallback, catalog/access/config services), ModelProviders/ (Azure Foundry adapter)
  EnterpriseAIPlatform.Web               # Blazor host, authZ policies, health/whoami/admin endpoints; Endpoints/ModelAccess (spec 014 routes), Endpoints/UserPreferences (spec 014 401 fix)
tests/
  EnterpriseAIPlatform.UnitTests         # downgrade, role mapping, hashing, no-session (SC-001/003/005/008); model-access evaluator, catalog/message-limit/persona-gen validation, cache fallback (spec 014)
  EnterpriseAIPlatform.IntegrationTests  # route + admin gating via WebApplicationFactory (SC-006/007); admin gate, soft delete, read/write asymmetry, catalog metadata, preferences 401 (spec 014)
  EnterpriseAIPlatform.ArchitectureTests # one-implementation-per-concern guard (SC-002); single access-computation/provider-adapter guard (spec 014)
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

## Status (spec 002)

Implemented + tested: US1 (single-source downgrade), US2 (structured no-session), US4 (role mapping), US5 (route/admin gating), US6 (identity hashing).
Follow-up: US3 (token-refresh → forced re-auth) is satisfied by Microsoft.Identity.Web's challenge-on-expiry today; its full FR-007 behavior lands when downstream token acquisition is added (spec 004).

## Status (spec 014)

**R1 scope implemented + tested** (86/86 tests passing): US1 (admin gate on all mutation endpoints), US2 (effective-access computation + soft delete), US3 (message-limit/persona-generation-model read-open/write-gated asymmetry), US4 (server-side re-validation), US5 (`/api/user/preferences/*` 401 fix), US6 (catalog metadata, single Azure/Foundry provider), US8 (workload-identity provider auth).

**Explicitly deferred to R2** — not started, not forgotten: US7's remaining provider adapters (Claude, Vertex, DeepSeek, Llama, Mistral, Kimi) against the existing `IModelProviderAdapter` seam, and the admin config UI (all R1 mutation endpoints are API-only). SC-008 (multi-provider client-code claim) is untestable until those adapters exist.

A pre-existing walking-skeleton bug surfaced while building 014's admin write endpoints: `UseStatusCodePagesWithReExecute("/not-found", ...)` re-executed *any* non-2xx API response (403/404/etc.) against the Blazor not-found page, and since that page only supports GET/HEAD/POST, PUT/DELETE admin requests were corrupted into a spurious 405. Fixed by scoping that middleware to non-`/api` requests in `Program.cs`.
