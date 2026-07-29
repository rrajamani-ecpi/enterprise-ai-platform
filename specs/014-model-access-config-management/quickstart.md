# Quickstart: Validating Model & Access Configuration Management (R1 subset)

**Feature**: 014-model-access-config-management | **Date**: 2026-07-28

This is a **validation/run guide** for the R1-scoped subset (US1–US6 single-provider, US8; US7's remaining provider adapters and the admin UI are R2). Implementation steps belong to `tasks.md` (produced by `/speckit-tasks`).

## Prerequisites

- Spec 002's walking skeleton running (Entra sign-in, `ICurrentUserAccessor`, `RequireAdmin` policy) — this feature builds on it directly.
- An Azure SQL Database (or `mcr.microsoft.com/mssql/server` container / LocalDB for dev) for the EF Core admin/system config tables.
- An Azure Cache for Redis instance (or local Redis container for dev) for the system-config cache.
- A Microsoft Foundry / Azure OpenAI deployment reachable via workload/managed identity (or a local dev identity via `az login` + `DefaultAzureCredential`) — for the R1 `AzureFoundryProviderAdapter`.
- Test users mapped (via spec 002) to at least two different roles, one with `AdvancedModelAccess=true` and one without.

## Setup

```bash
dotnet restore
dotnet build EnterpriseAIPlatform.sln
dotnet ef database update --project src/EnterpriseAIPlatform.Infrastructure   # apply ModelAccess migrations
# configure SQL connection string, Redis connection string, and Foundry endpoint via user-secrets / appsettings.Development.json (no secrets in source)
dotnet run --project src/EnterpriseAIPlatform.Web
```

## Validation scenarios (one per in-scope user story)

| Story | Steps | Expected (pass) |
|---|---|---|
| **1 · Admin gate** (P1) | As a non-admin, call `PUT /api/admin/model-config/{id}`, `PUT /api/admin/system-config`, `PUT /api/admin/config/message-limit`, `PUT /api/admin/config/persona-generation-model` directly. Repeat as admin. | Non-admin: every call rejected, no document changed (SC-001). Admin: every call succeeds. |
| **2 · Effective access + soft delete** (P1) | Configure a model with `requiresAdvancedModelAccess=true`, enabled, in role A's allow-list only. Call `GET /api/model-access/available-models` as a role-A user with and without `AdvancedModelAccess`. Then soft-delete the model via `DELETE /api/admin/model-config/{id}`. | With `AdvancedModelAccess`: model included. Without: excluded (SC-002). After delete: `GET /api/model-catalog` excludes it, but the underlying row still exists with `IsDeleted=true` (SC-003). |
| **3 · Read-open, write-gated** (P2) | As a non-admin, call `GET /api/config/message-limit` and `GET /api/config/persona-generation-model`, then attempt `PUT` on each. | Reads succeed; writes rejected (SC-004). Admin writes succeed. |
| **4 · Server-side re-validation** (P2) | Submit `PUT /api/admin/config/message-limit` with cap `0`, a negative number, and a non-integer. Submit a persona-generation model id present in the registry but not the allow-list. | All rejected server-side, previous config unchanged (SC-005). A valid ≥1 integer cap and an allow-listed model id both succeed. |
| **5 · Preferences 401** (P3) | With no session, call any `/api/user/preferences/*` route. | 401 with the `UNAUTHORIZED` `ServerActionResponse` shape — never 500 (SC-006). |
| **6 · Catalog metadata** (P3, R1 = single provider) | `GET /api/model-catalog` for the one R1-registered Azure/Foundry model. | Entry includes canonical `provider:modelId`, display name, provider, all three capability flags, and access tier (SC-007, single-provider scope). |
| **8 · Workload identity** (P1) | Inspect the R1 `AzureFoundryProviderAdapter` configuration and any client-delivered config. | Authenticates via `DefaultAzureCredential`/managed identity; no static API key present anywhere client-reachable (SC-009). |

**Deferred to R2** (not exercised by this quickstart): US7's remaining provider adapters (SC-008 multi-provider claim) and any admin-UI-driven scenario.

## Automated test commands

```bash
dotnet test tests/EnterpriseAIPlatform.UnitTests          # access-computation matrix (SC-002), validation (SC-005)
dotnet test tests/EnterpriseAIPlatform.IntegrationTests   # admin gate, soft delete, read/write asymmetry, preferences 401
dotnet test tests/EnterpriseAIPlatform.ArchitectureTests  # single IModelProviderAdapter per provider, single access computation
```

## Done when

- [ ] All seven R1-scoped scenarios pass manually.
- [ ] All three test projects green, covering SC-001…SC-007 and SC-009 (R1 subset).
- [ ] Architecture test confirms exactly one effective-access computation and one adapter per registered provider (Principle IV).
- [ ] SC-008 (multi-provider) and full SC-010 cache-fallback drill are explicitly deferred to R2 tasks, not silently skipped.
