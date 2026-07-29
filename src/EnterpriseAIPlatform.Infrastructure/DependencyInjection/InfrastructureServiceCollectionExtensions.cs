using EnterpriseAIPlatform.Application.Authorization;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Infrastructure.Authentication;
using EnterpriseAIPlatform.Infrastructure.Identity;
using EnterpriseAIPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseAIPlatform.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the canonical session/role/identity services (spec 002). Every consumer resolves
/// these single implementations — the guarantee behind Constitution Principle IV.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RoleDerivationMappingOptions>()
            .Bind(configuration.GetSection(RoleDerivationMappingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<PublicRoutesOptions>()
            .Bind(configuration.GetSection(PublicRoutesOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<CosmosOptions>()
            .Bind(configuration.GetSection(CosmosOptions.SectionName));

        services.AddHttpContextAccessor();
        services.AddSingleton<IRoleResolver, RoleResolver>();
        services.AddSingleton<IClaimsTransformation, RoleClaimsTransformation>();
        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.AddSingleton<IIdentityHasher, IdentityHasher>();
        services.AddSingleton<CosmosClientProvider>();

        return services;
    }
}
