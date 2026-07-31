using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EnterpriseAIPlatform.Infrastructure.HealthChecks;

/// <summary>
/// The single Azure Key Vault health check (spec 017 FR-004/006). Reports Healthy immediately,
/// without attempting a connection, when unconfigured (D6) — startup validation
/// (<c>AddSupportInfrastructure</c>) already guarantees this only happens in Development. Never
/// includes the underlying exception in the result (FR-005).
/// </summary>
public sealed class KeyVaultHealthCheck : IHealthCheck
{
    private readonly KeyVaultOptions _options;

    public KeyVaultHealthCheck(IOptions<KeyVaultOptions> options) => _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.VaultUri))
        {
            return HealthCheckResult.Healthy("not configured (dev)");
        }

        try
        {
            var client = new SecretClient(new Uri(_options.VaultUri), new DefaultAzureCredential());
            var page = client.GetPropertiesOfSecretsAsync(cancellationToken).AsPages(pageSizeHint: 1);
            await foreach (var _ in page.WithCancellation(cancellationToken))
            {
                break;
            }

            return HealthCheckResult.Healthy();
        }
        catch (Exception)
        {
            // Deliberately no exception/message passed to HealthCheckResult (FR-005).
            return HealthCheckResult.Unhealthy();
        }
    }
}
