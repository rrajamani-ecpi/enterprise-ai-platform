using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Infrastructure.ModelAccess;
using EnterpriseAIPlatform.Infrastructure.ModelProviders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseAIPlatform.Infrastructure.DependencyInjection;

/// <summary>
/// Registers spec 014's model-access/config-management services. Layer 1 of the sequencing plan —
/// depends only on spec 002's <c>AddPlatformInfrastructure</c> having already run.
/// </summary>
public static class ModelAccessServiceCollectionExtensions
{
    public static IServiceCollection AddModelAccessInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ModelAccessSqlOptions>()
            .Bind(configuration.GetSection(ModelAccessSqlOptions.SectionName));

        services.AddOptions<ModelAccessCacheOptions>()
            .Bind(configuration.GetSection(ModelAccessCacheOptions.SectionName));

        services.AddOptions<AzureFoundryOptions>()
            .Bind(configuration.GetSection(AzureFoundryOptions.SectionName));

        // Azure SQL via EF Core for the strongly relational admin/system config entities
        // (constitution Data & Storage). Lazy — the app boots without a live SQL dependency,
        // mirroring spec 002's CosmosClientProvider.
        var sqlConnectionString = configuration[$"{ModelAccessSqlOptions.SectionName}:ConnectionString"];
        services.AddDbContext<ModelAccessDbContext>(options =>
            options.UseSqlServer(string.IsNullOrWhiteSpace(sqlConnectionString) ? " " : sqlConnectionString));

        // Azure Cache for Redis fronting SystemModelConfig reads (FR-014). Tests replace this
        // registration with AddDistributedMemoryCache() via ConfigureTestServices.
        var redisConnectionString = configuration[$"{ModelAccessCacheOptions.SectionName}:RedisConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddStackExchangeRedisCache(options => options.Configuration = redisConnectionString);
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        services.AddScoped<ISystemModelConfigStore, EfSystemModelConfigStore>();
        services.AddScoped<ISystemModelConfigCache, RedisSystemModelConfigCache>();
        services.AddScoped<IModelAccessService, ModelAccessService>();
        services.AddScoped<IModelCatalogService, ModelCatalogService>();
        services.AddScoped<IMessageLimitConfigService, MessageLimitConfigService>();
        services.AddScoped<IPersonaGenerationModelConfigService, PersonaGenerationModelConfigService>();

        // R1 registers exactly one provider adapter; R2 adds more against the same interface
        // (resolved as IEnumerable<IModelProviderAdapter>, selected by Provider key).
        services.AddSingleton<IModelProviderAdapter, AzureFoundryProviderAdapter>();

        return services;
    }
}
