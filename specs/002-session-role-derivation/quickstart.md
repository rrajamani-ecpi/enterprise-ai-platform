# Quickstart: Validating Session Resolution & Role Derivation

**Feature**: 002-session-role-derivation | **Date**: 2026-07-21

This is a **validation/run guide**, not implementation. It proves the six user stories end-to-end against the target stack. Implementation steps belong to `tasks.md` (produced by `/speckit-tasks`).

## Prerequisites

- .NET 10 SDK.
- A Microsoft Entra ID app registration (test tenant) with: OIDC redirect URIs for the app, the **groups** claim enabled, and at least three test users mapped to the Admin / Employee / Student groups (plus one in no group).
- An Azure Cosmos DB account (or the Cosmos emulator) for the user-scoped container.
- Configuration: the group-GUID → role-flag mapping and the public-route allow-list (bound via Options, validated at startup).
- Secrets (client secret / cert) in Azure Key Vault or user-secrets — never in source.

## Setup

```bash
dotnet restore
dotnet build EnterpriseAIPlatform.sln
# configure Entra + Cosmos via user-secrets / appsettings.Development.json (no secrets in source)
dotnet run --project src/EnterpriseAIPlatform.Web
```

## Validation scenarios (one per user story)

| Story | Steps | Expected (pass) |
|---|---|---|
| **1 · Single-source downgrade** (P1) | Sign in as an admin, activate Student View so `ImpersonateAsStudent=true`. Exercise the claims-transformation path, the current-user accessor, and any server action. | All report `IsAdmin=false, IsEmployee=false, IsContractor=false, IsStudent=true` (SC-001). Change the shared downgrade once → all paths reflect it without editing call sites (SC-002). |
| **2 · Structured no-session** (P2) | Invoke `ICurrentUserAccessor.GetCurrentUser()` with no active session (e.g., background/unauthenticated context). | Returns `ServerActionResponse` with `Status=UNAUTHORIZED`; **no** thrown exception (SC-003). |
| **3 · Token-refresh failure** (P2) | Force a refresh-token failure for an active session; trigger the next session check. | User is redirected into the Entra re-auth prompt; after re-auth a fresh session has re-derived flags (SC-004). |
| **4 · Deterministic role mapping** (P3) | Sign in with each fixture: admin group, no group, multiple groups. | Flags match the configured mapping in every case; multiple groups set multiple flags independently (SC-005). |
| **5 · Server-side route/admin gating** (P2) | Unauthenticated: request each public route, then a protected route. Non-admin: request an admin route/action even with a UI that exposes the control. | Public routes served; protected routes redirect to sign-in; admin route/action rejected server-side (SC-006/007). |
| **6 · Hashed partition keys** (P3) | Sign in; inspect the partition key used to persist user data. Repeat with different email casing/whitespace. | Key is a SHA-256 hash of the normalized email (never raw); normalized-equivalent emails yield the identical key (SC-008). |

## Automated test commands

```bash
dotnet test tests/EnterpriseAIPlatform.UnitTests          # role mapping, downgrade idempotency/fail-closed, hash determinism
dotnet test tests/EnterpriseAIPlatform.IntegrationTests   # route protection, admin gating, no-session response shape
dotnet test tests/EnterpriseAIPlatform.ArchitectureTests  # single downgrade impl + single current-user resolver (SC-002)
```

## Done when

- [ ] All six scenarios pass manually.
- [ ] All three test projects green, covering SC-001 … SC-008.
- [ ] Architecture test confirms exactly one downgrade implementation and one current-user resolver (Principle IV).
