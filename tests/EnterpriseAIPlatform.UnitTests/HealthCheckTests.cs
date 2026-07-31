using EnterpriseAIPlatform.Infrastructure.HealthChecks;
using EnterpriseAIPlatform.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>Spec 017 US2 / FR-004-007 / D6: unconfigured dependencies report Healthy without attempting a connection.</summary>
public class HealthCheckTests
{
    [Fact]
    public async Task CosmosHealthCheck_Unconfigured_ReportsHealthy_WithoutAttemptingConnection()
    {
        var provider = new CosmosClientProvider(Options.Create(new CosmosOptions { AccountEndpoint = "" }));
        var check = new CosmosHealthCheck(provider, Options.Create(new CosmosOptions { AccountEndpoint = "" }));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task KeyVaultHealthCheck_Unconfigured_ReportsHealthy_WithoutAttemptingConnection()
    {
        var check = new KeyVaultHealthCheck(Options.Create(new KeyVaultOptions { VaultUri = "" }));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
}
