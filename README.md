# enterprise-ai-platform

Enterprise AI Platform — a spec-driven (GitHub spec-kit) build. Specifications live under [`specs/`](./specs/); the platform constitution (principles + technology stack) is in [`.specify/memory/constitution.md`](./.specify/memory/constitution.md); the release plan is in [`docs/spec-sequencing-plan.md`](./docs/spec-sequencing-plan.md).

## Release 1 — Authenticated Enterprise Chat (in progress)

The first increment is the **walking skeleton**: the canonical server-side session/role/authorization layer from spec 002, which every later feature builds on.

## Tech stack

- .NET 10 (LTS), C#
- Blazor Web App (Interactive Server render mode)
- Microsoft.Identity.Web + Microsoft Entra ID (OIDC)
- Azure Cosmos DB (user-scoped storage), Azure Key Vault, workload identity
- OpenTelemetry + Azure Monitor / Application Insights
- Tests: xUnit, NSubstitute, `WebApplicationFactory`

## Solution layout

```text
src/
  EnterpriseAIPlatform.Domain           # role flags, identity value objects
  EnterpriseAIPlatform.Application       # contracts + ServerActionResponse, RoleDowngrade, PolicyNames
  EnterpriseAIPlatform.Infrastructure    # Entra claims transformation, current-user accessor, role resolver, identity hasher, Cosmos, telemetry
  EnterpriseAIPlatform.Web               # Blazor host, authZ policies, health/whoami/admin endpoints
tests/
  EnterpriseAIPlatform.UnitTests         # downgrade, role mapping, hashing, no-session (SC-001/003/005/008)
  EnterpriseAIPlatform.IntegrationTests  # route + admin gating via WebApplicationFactory (SC-006/007)
  EnterpriseAIPlatform.ArchitectureTests # one-implementation-per-concern guard (SC-002)
```

## Build, test, run

```bash
dotnet build
dotnet test
dotnet run --project src/EnterpriseAIPlatform.Web
```

### Configuration (no secrets in source)

Set these before running against a real tenant (use user-secrets or environment variables):

- `AzureAd:TenantId`, `AzureAd:ClientId` — Entra app registration (enable the **groups** claim).
- `RoleDerivation:Mappings` — Entra group-GUID → role (`Admin` / `Employee` / `Contractor` / `Student`). Validated at startup.
- `PublicRoutes:Paths` — the explicit anonymous allow-list (health, sign-in, LMS launch/error).
- `Cosmos:AccountEndpoint`, `ApplicationInsights:ConnectionString` — optional; the app boots without them in local dev.

## Status (spec 002)

Implemented + tested: US1 (single-source downgrade), US2 (structured no-session), US4 (role mapping), US5 (route/admin gating), US6 (identity hashing).
Follow-up: US3 (token-refresh → forced re-auth) is satisfied by Microsoft.Identity.Web's challenge-on-expiry today; its full FR-007 behavior lands when downstream token acquisition is added (specs 004/014).
