using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseAIPlatform.Infrastructure.Telemetry;

/// <summary>
/// Structured telemetry bootstrap (Constitution Principle IV — one logging discipline; WAF
/// Operational Excellence). Azure Monitor / Application Insights is enabled only when a
/// connection string is configured, so local dev runs without it.
/// </summary>
public static class TelemetryExtensions
{
    public static IServiceCollection AddPlatformTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration["ApplicationInsights:ConnectionString"]
                               ?? configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddOpenTelemetry().UseAzureMonitor(options => options.ConnectionString = connectionString);
        }

        return services;
    }
}
