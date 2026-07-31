# Contract: Service Interfaces

**Feature**: 017-feedback-changelog-health-probes

Internal C# interfaces. Signatures are the contract; bodies belong to implementation/tasks. Reuses spec 002's `ServerActionResponse<T>`/`UserModel`/`IIdentityHasher` and spec 004's `IChatThreadStore` rather than redefining equivalents (Principle IV).

## `IChangelogReader` (FR-001–003, D1/D2)

```csharp
public interface IChangelogReader
{
    // Never throws. Empty list if the source is missing, empty, or entirely unparseable.
    Task<IReadOnlyList<ChangelogEntry>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
```

- **Contract**: Entries ordered newest-first by `Version`. One implementation (architecture-tested), `FileSystemChangelogReader`.

## `AlertWindowEvaluator` (FR-011, D3) — pure static function

```csharp
public static class AlertWindowEvaluator
{
    public static bool ShouldShowAlert(
        ChangelogEntry? latest, VersionAcknowledgmentModel? acknowledgment, DateTimeOffset now);
}
```

- **Contract**: `true` when `latest is not null && (acknowledgment is null || now - acknowledgment.AcknowledgedAtUtc > TimeSpan.FromDays(60))`. No I/O — framework-free, mirrors spec 014's `ModelAccessEvaluator`/spec 006's `MultiChatQuadrantRules`.

## `IVersionAcknowledgmentStore` (FR-012)

```csharp
public interface IVersionAcknowledgmentStore
{
    Task<VersionAcknowledgmentModel?> GetAsync(string ownerPartitionKey, CancellationToken cancellationToken = default);

    Task SetAsync(
        string ownerPartitionKey, string acknowledgedVersion, CancellationToken cancellationToken = default);
}
```

- **Contract**: `GetAsync` returns `null` (not an error) for a caller with no prior acknowledgment. One implementation, `CosmosVersionAcknowledgmentStore` (spec 002's `users` container).

## `IFeedbackForwarder` (FR-009/010, D7)

```csharp
public interface IFeedbackForwarder
{
    // Never throws to the caller — catches and logs internally (FR-010). Returns whether the
    // forward actually succeeded, purely for telemetry; the HTTP endpoint ignores this value
    // when deciding what to tell the user (always success once ownership passes).
    Task<bool> ForwardAsync(string threadId, string content, CancellationToken cancellationToken = default);
}
```

- **Contract**: One implementation, `EcpiFeedbackForwarder` — plain `HttpClient` POST to a configured endpoint/API key; any failure is caught and logged, never propagated.

## Health checks (FR-004–007, D5/D6) — `Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck`, not a new abstraction

```csharp
// CosmosHealthCheck, KeyVaultHealthCheck : IHealthCheck (framework interface, no new contract).
// Each: HealthCheckResult.Healthy("not configured (dev)") immediately if unconfigured (D6);
// otherwise a real, timeout-bounded connectivity check, mapped to Healthy/Unhealthy — never
// echoing the underlying exception into the result's Description (that's what the response
// writer would otherwise serialize, violating FR-005).
```

- **Contract**: Registered with `Timeout = TimeSpan.FromSeconds(5)` (FR-006); Cosmos tagged `"live"` + `"ready"`, Key Vault tagged `"ready"` only (FR-007).

## Reused from specs 002/004 (not redefined here)

- `ServerActionResponse<T>` / `UserModel` / `StoragePartitionKey` / `IIdentityHasher` (spec 002).
- `IChatThreadStore.GetAsync` (spec 004) — feedback's ownership check.
- `CosmosOptions` / `CosmosClientProvider` (spec 002) — the `users` container backing `IVersionAcknowledgmentStore`.
