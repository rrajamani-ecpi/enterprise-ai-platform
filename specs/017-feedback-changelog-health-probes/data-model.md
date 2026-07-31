# Phase 1 Data Model: Feedback, Changelog & Health Probes

**Feature**: 017-feedback-changelog-health-probes | **Date**: 2026-07-29

R1-scoped entities only (US1–US4). Source of truth: [spec.md](./spec.md) Key Entities; decisions from [research.md](./research.md).

---

## ChangelogEntry  *(file-system-sourced, not a database row)*

| Field | Type | Notes |
|---|---|---|
| `Version` | `System.Version` | Parsed from the source filename (D2). |
| `Content` | string | The file's Markdown body. |

**Source**: one file per version under `Changelog:ContentDirectory` (D1). Missing directory / unparseable file ⇒ excluded from the returned list, never a thrown exception.

---

## VersionAcknowledgmentModel  *(Cosmos document, in spec 002's `users` container)*

| Field | Type | Notes |
|---|---|---|
| `Id` | string | The owner's `PartitionKey` (singleton per user, same pattern as spec 006's `multichat:{partitionKey}` deterministic id). |
| `PartitionKey` | string | Spec 002 `StoragePartitionKey` (hashed owner identity). |
| `AcknowledgedVersion` | string | The version string acknowledged (metadata only — not used in the D3 window calculation itself). |
| `AcknowledgedAtUtc` | DateTimeOffset | The timestamp the 60-day cooldown (D3) is measured from. |

**Read path**: `GET /api/changelog/acknowledgment` returns `null` (not an error) if no row exists yet — a brand-new user has never acknowledged anything, which `AlertWindowEvaluator` (D3) already treats as "show the alert" without special-casing.

---

## FeedbackSubmission  *(request shape, never persisted — FR-009)*

| Field | Type | Notes |
|---|---|---|
| `ThreadId` | string | Verified against the caller's own thread via spec 004's `IChatThreadStore` before any forwarding (FR-008). |
| `Content` | string | Forwarded verbatim to the external ECPI API; never written to this application's own store. |

---

## HealthCheckResult  *(computed per probe call, not persisted)*

The framework's own `Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport`/`HealthReportEntry`, serialized through a custom response writer (D5) that emits only:

| Field | Type | Notes |
|---|---|---|
| `status` | string | The aggregate `Healthy`/`Degraded`/`Unhealthy`. |
| `checks[].name` | string | The registered check name (e.g. `cosmos`, `keyvault`) — identifies the failing dependency (FR-005). |
| `checks[].status` | string | That check's individual status. |

**Invariant**: `HealthReportEntry.Exception`/`Description` are never serialized — this is where raw provider error text would otherwise leak (FR-005/SC-002).

---

## Reused from specs 002/004 (not redefined here)

- `ServerActionResponse<T>` / `UserModel` / `StoragePartitionKey` / `IIdentityHasher` (spec 002) — caller identity for feedback ownership and acknowledgment partitioning.
- `IChatThreadStore.GetAsync` (spec 004) — the ownership check feedback reuses rather than re-deriving (FR-008).
- `CosmosOptions.UserContainerName` (spec 002, declared but previously unused by any concrete store) — now backs `VersionAcknowledgmentModel`.

## Relationships

```mermaid
erDiagram
    UserModel ||--o| VersionAcknowledgmentModel : "PartitionKey (spec 002 hashed identity)"
    ChangelogEntry ||--|| VersionAcknowledgmentModel : "AlertWindowEvaluator compares latest vs. AcknowledgedAtUtc"
    FeedbackSubmission }o--|| ChatThreadModel : "ThreadId ownership check (spec 004 IChatThreadStore)"
    HealthCheckResult ||--|| CosmosHealthCheck : "checks[].name = \"cosmos\""
    HealthCheckResult ||--|| KeyVaultHealthCheck : "checks[].name = \"keyvault\""
```
