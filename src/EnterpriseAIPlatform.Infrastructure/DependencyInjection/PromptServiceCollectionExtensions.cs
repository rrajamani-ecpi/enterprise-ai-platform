using EnterpriseAIPlatform.Application.Prompts;
using EnterpriseAIPlatform.Infrastructure.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseAIPlatform.Infrastructure.DependencyInjection;

/// <summary>
/// Registers spec 016's prompt CRUD + sharing + ownership-transfer services. Depends on spec 002's
/// <c>AddPlatformInfrastructure</c> (for <c>IIdentityHasher</c>/<c>ICurrentUserAccessor</c>) and
/// spec 018's <c>AddSharing</c> (for <c>ISharingPolicyService</c>) having already run.
/// </summary>
public static class PromptServiceCollectionExtensions
{
    public static IServiceCollection AddPromptInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Lazy connection string — the app boots without a live SQL dependency, mirroring spec 009's
        // PersonaDbContext registration.
        var sqlConnectionString = configuration["PromptSql:ConnectionString"];
        services.AddDbContext<PromptDbContext>(options =>
            options.UseSqlServer(string.IsNullOrWhiteSpace(sqlConnectionString) ? " " : sqlConnectionString));

        services.AddScoped<IPromptService, PromptService>();

        return services;
    }
}
