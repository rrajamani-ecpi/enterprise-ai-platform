using System.Collections.Concurrent;
using EnterpriseAIPlatform.Application.Support;
using EnterpriseAIPlatform.Domain.Support;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EnterpriseAIPlatform.IntegrationTests;

/// <summary>In-memory fake for <see cref="IVersionAcknowledgmentStore"/> — no live Cosmos dependency in tests.</summary>
public sealed class FakeVersionAcknowledgmentStore : IVersionAcknowledgmentStore
{
    private readonly ConcurrentDictionary<string, VersionAcknowledgmentModel> _acknowledgments = new();

    public bool ThrowOnSet { get; set; }

    public Task<VersionAcknowledgmentModel?> GetAsync(string ownerPartitionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(_acknowledgments.TryGetValue(ownerPartitionKey, out var ack) ? ack : null);

    public Task SetAsync(string ownerPartitionKey, string acknowledgedVersion, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSet)
        {
            throw new InvalidOperationException("Simulated Cosmos write failure.");
        }

        _acknowledgments[ownerPartitionKey] = new VersionAcknowledgmentModel
        {
            Id = ownerPartitionKey, PartitionKey = ownerPartitionKey,
            AcknowledgedVersion = acknowledgedVersion, AcknowledgedAtUtc = DateTimeOffset.UtcNow,
        };
        return Task.CompletedTask;
    }
}

/// <summary>Deterministic fake for <see cref="IFeedbackForwarder"/> — no live ECPI dependency in tests.</summary>
public sealed class FakeFeedbackForwarder : IFeedbackForwarder
{
    public bool ShouldSucceed { get; set; } = true;

    public List<(string ThreadId, string Content)> Calls { get; } = new();

    public Task<bool> ForwardAsync(string threadId, string content, CancellationToken cancellationToken = default)
    {
        Calls.Add((threadId, content));
        return Task.FromResult(ShouldSucceed);
    }
}

/// <summary>
/// A deterministically-failing <see cref="IHealthCheck"/> used to verify the sanitizing response
/// writer never leaks <see cref="HealthCheckResult.Description"/>/<see cref="HealthCheckResult.Exception"/>
/// (spec 017 FR-005) — avoids pointing real Cosmos/Key Vault checks at a genuinely broken endpoint.
/// </summary>
public sealed class FakeFailingHealthCheck : IHealthCheck
{
    public const string SensitiveMarker = "Server=tcp:internal-db.example.com;Password=super-secret";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(HealthCheckResult.Unhealthy(SensitiveMarker, new InvalidOperationException(SensitiveMarker)));
}
