using EnterpriseAIPlatform.Application.Support;
using EnterpriseAIPlatform.Infrastructure.HealthChecks;
using EnterpriseAIPlatform.Infrastructure.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EnterpriseAIPlatform.Infrastructure.DependencyInjection;

/// <summary>
/// Registers spec 017's support features (changelog, health probes, feedback proxy, version-alert
/// acknowledgment). Depends on spec 002's <c>AddPlatformInfrastructure</c> (Cosmos, identity) and
/// spec 004's <c>AddChatInfrastructure</c> (thread ownership for feedback) having already run.
/// </summary>
public static class SupportServiceCollectionExtensions
{
    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(5);

    public static IServiceCollection AddSupportInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ChangelogOptions>().Bind(configuration.GetSection(ChangelogOptions.SectionName));
        services.AddOptions<FeedbackOptions>().Bind(configuration.GetSection(FeedbackOptions.SectionName));

        services.AddOptions<KeyVaultOptions>()
            .Bind(configuration.GetSection(KeyVaultOptions.SectionName))
            .Validate<IHostEnvironment>(
                (options, env) => !string.IsNullOrWhiteSpace(options.VaultUri) || !env.IsProduction(),
                "KeyVault:VaultUri must be configured in Production — the Key Vault health check may not be silently bypassed outside local development.")
            .ValidateOnStart();

        services.AddSingleton<IChangelogReader, FileSystemChangelogReader>();
        services.AddScoped<IVersionAcknowledgmentStore, CosmosVersionAcknowledgmentStore>();

        services.AddHttpClient<EcpiFeedbackForwarder>();
        services.AddScoped<IFeedbackForwarder>(sp => sp.GetRequiredService<EcpiFeedbackForwarder>());

        services.AddHealthChecks()
            .AddCheck<CosmosHealthCheck>("cosmos", tags: new[] { "live", "ready" }, timeout: HealthCheckTimeout)
            .AddCheck<KeyVaultHealthCheck>("keyvault", tags: new[] { "ready" }, timeout: HealthCheckTimeout);

        return services;
    }
}
