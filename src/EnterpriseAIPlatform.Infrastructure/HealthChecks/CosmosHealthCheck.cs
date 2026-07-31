using EnterpriseAIPlatform.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EnterpriseAIPlatform.Infrastructure.HealthChecks;

/// <summary>
/// The single Cosmos DB health check (spec 017 FR-004/006/007). Reports Healthy immediately,
/// without attempting a connection, when unconfigured (D6) — every existing test factory leaves
/// <c>Cosmos:AccountEndpoint</c> unset, and this keeps spec 002's existing health-route test green.
/// Never includes the underlying exception in the result (FR-005) — the response writer only ever
/// sees <see cref="HealthCheckResult.Status"/>, never <see cref="HealthCheckResult.Exception"/>/<see cref="HealthCheckResult.Description"/>.
/// </summary>
public sealed class CosmosHealthCheck : IHealthCheck
{
    private readonly CosmosClientProvider _clientProvider;
    private readonly CosmosOptions _options;

    public CosmosHealthCheck(CosmosClientProvider clientProvider, IOptions<CosmosOptions> options)
    {
        _clientProvider = clientProvider;
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.AccountEndpoint))
        {
            return HealthCheckResult.Healthy("not configured (dev)");
        }

        try
        {
            await _clientProvider.Client.ReadAccountAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception)
        {
            // Deliberately no exception/message passed to HealthCheckResult (FR-005).
            return HealthCheckResult.Unhealthy();
        }
    }
}
