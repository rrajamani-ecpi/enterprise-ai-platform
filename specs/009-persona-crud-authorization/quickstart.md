# Quickstart: Validating Persona CRUD & Authorization

**Feature**: 009-persona-crud-authorization | **Date**: 2026-08-31

This is a **validation/run guide**. Implementation steps belong to `tasks.md` (produced by `/speckit-tasks`).

## Prerequisites

- Spec 002's walking skeleton running (Entra sign-in in production; `TestAuthHandler` for integration tests) — this feature builds on it directly.
- An Azure SQL Database (or `mcr.microsoft.com/mssql/server` container / LocalDB for dev) for the new `PersonaDbContext` migrations.
- `TestAuthHandler` extended with an `X-Test-Roles` header (comma-separated `RoleFlags` names — research.md D9) so Employee/Contractor/Student callers can be simulated over HTTP, alongside the existing `X-Test-User`/`X-Test-Admin`.

## Setup

```bash
dotnet restore
dotnet build EnterpriseAIPlatform.slnx
dotnet ef database update --context PersonaDbContext --project src/EnterpriseAIPlatform.Infrastructure   # apply Persona migrations
dotnet run --project src/EnterpriseAIPlatform.Web
```

## Validation scenarios (one per user story)

| Story | Steps | Expected (pass) |
|---|---|---|
| **1 · Ownership transfer never loses a persona** (P1) | `POST /api/personas/{id}/transfer-ownership` as admin with `newOwnerEmail`. Inject a failure (e.g. a broken connection) immediately after the `UPDATE` would commit, and again immediately before. Retry after each. | The persona always exists exactly once — either still under the original owner (failure before commit) or fully under the new owner (failure after commit or success) — never both, never neither (SC-001). A retry after either outcome succeeds without manual intervention, and does not create a duplicate (research.md D1 — no delete-then-recreate step exists to duplicate). |
| **2 · Access, edit, and delete rights are consistently gated** (P1) | As admin, owner, a designated collaborator, an eligible student (on a lesson persona), and an unrelated authenticated user (via `X-Test-User`/`X-Test-Roles`), call `GET`/`PATCH`/`DELETE /api/personas/{id}` and `POST /api/personas/{id}` (group-token share) directly against the API. | Unrelated/forbidden callers get the fixed `401 Unauthorized` shape (contracts/authorization-policies.md), identical whether the persona exists or not (SC-002). Student gets read-only on a lesson persona (SC-002). Non-admin — including the designated collaborator — is blocked from editing/deleting a lesson persona (SC-003). A non-admin's submitted `isLessonPersona` change is silently discarded (SC-004). A non-admin's `GET /api/personas` list excludes lesson personas (SC-005). A non-admin sharing with a group token is rejected (FR-008); admin succeeds. |
| **3 · The A2A credential never reaches client code** (P2) | Inspect every `IPersonaService` method's return type. Confirm `IPersonaRawAccessor.GetRawAsync` is `internal` and has no `InternalsVisibleTo` grant to `EnterpriseAIPlatform.Web`. | Every `IPersonaService`-reachable path returns `PersonaPublicDTO`, which has no `ApiKey` property at all — verified by a reflection check (`PersonaSingleImplementationTests`), and `IPersonaRawAccessor` is a compile-time error to reference from `Web` (SC-006). Note: the "rendered component state" half of SC-006 is not yet exercisable — no Blazor component for personas exists in this tasks.md (see tasks.md's Scope note). |
| **4 · `dataProducts` requirement is enforced for every entry point** (P3) | `POST`/`PATCH /api/personas` directly with `extensions: ["DataProduct"]` and `dataProducts: []` (and `dataProducts` omitted entirely). Then with at least one entry. Then without the `DataProduct` extension and an empty `dataProducts`. | First two rejected with a field-specific validation error, before persistence (SC-007). Third succeeds (populated `dataProducts`). Fourth succeeds (extension not selected, rule doesn't apply). |
| **Concurrent edit during transfer** (Edge Case, resolved via clarification) | Start a `PATCH /api/personas/{id}` and a `POST .../transfer-ownership` concurrently against the same persona (read both before either commits). | Whichever commits second receives the `409 Conflict` shape (contracts/authorization-policies.md), never a silent overwrite and never queued-then-applied. |

## Automated test commands

```bash
dotnet test tests/EnterpriseAIPlatform.UnitTests --filter FullyQualifiedName~Personas          # PersonaAccessEvaluator matrix (SC-002/003), PersonaExtensionRules (SC-007)
dotnet test tests/EnterpriseAIPlatform.IntegrationTests --filter FullyQualifiedName~Personas    # full CRUD+auth HTTP vertical, transfer conflict (SC-001, SC-004, SC-005)
dotnet test tests/EnterpriseAIPlatform.ArchitectureTests --filter FullyQualifiedName~Persona    # single IPersonaService; PersonaPublicDTO has no ApiKey (SC-006)
```

## Done when

- [ ] All five scenarios above pass.
- [ ] All three test projects green, covering SC-001 through SC-007.
- [ ] Architecture test confirms `PersonaPublicDTO` has no `ApiKey` property and exactly one `IPersonaService` implementation (Principle IV).
- [ ] `TestAuthHandler`'s `X-Test-Roles` extension does not regress any existing admin/non-admin-only integration test.
