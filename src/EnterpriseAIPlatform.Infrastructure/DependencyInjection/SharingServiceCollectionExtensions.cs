using EnterpriseAIPlatform.Application.Sharing;
using EnterpriseAIPlatform.Infrastructure.Sharing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseAIPlatform.Infrastructure.DependencyInjection;

/// <summary>
/// Registers spec 018's sharing-policy evaluator. Depends only on spec 002's
/// <c>AddPlatformInfrastructure</c> having already run (for <see cref="EnterpriseAIPlatform.Application.Identity.UserModel"/>).
/// No database, cache, or HTTP endpoint — both config sections are static <c>IOptions</c>-bound
/// app config (spec 018 Clarifications; research.md D1).
/// </summary>
public static class SharingServiceCollectionExtensions
{
    public static IServiceCollection AddSharingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RoleSharingPolicyOptions>()
            .Bind(configuration.GetSection(RoleSharingPolicyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<GlobalSharingOverrideOptions>()
            .Bind(configuration.GetSection(GlobalSharingOverrideOptions.SectionName));

        services.AddScoped<ISharingPolicyService, SharingPolicyService>();

        return services;
    }
}
