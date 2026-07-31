using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Infrastructure.Chat;
using EnterpriseAIPlatform.Infrastructure.ModelProviders;
using EnterpriseAIPlatform.Infrastructure.Redaction;
using EnterpriseAIPlatform.Infrastructure.Safety;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EnterpriseAIPlatform.Infrastructure.DependencyInjection;

/// <summary>
/// Registers spec 004's chat pipeline. Depends on spec 002's <c>AddPlatformInfrastructure</c> and
/// spec 014's <c>AddModelAccessInfrastructure</c> having already run.
/// </summary>
public static class ChatServiceCollectionExtensions
{
    public static IServiceCollection AddChatInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ContentSafetyOptions>()
            .Bind(configuration.GetSection(ContentSafetyOptions.SectionName))
            .Validate<IHostEnvironment>(
                (options, env) => !string.IsNullOrWhiteSpace(options.Endpoint) || !env.IsProduction(),
                "ContentSafety:Endpoint must be configured in Production — Content Safety may not be silently bypassed outside local development.")
            .ValidateOnStart();

        services.AddScoped<IChatThreadStore, CosmosChatThreadStore>();
        services.AddScoped<IChatMessageStore, CosmosChatMessageStore>();
        services.AddSingleton<IDailyMessageCounter, DailyMessageCounter>();
        services.AddSingleton<RegexPiiRedactor>();
        services.AddSingleton<IPiiRedactor>(sp => sp.GetRequiredService<RegexPiiRedactor>());

        services.AddHttpClient<AzureContentSafetyGuard>();
        services.AddScoped<IContentSafetyGuard>(sp => sp.GetRequiredService<AzureContentSafetyGuard>());

        // Reuses spec 014's already-registered IModelProviderAdapter singleton (AzureFoundryProviderAdapter) — no duplicate registration.
        services.AddHttpClient<AzureFoundryChatCompletionClient>()
            .AddStandardResilienceHandler();
        services.AddScoped<IChatCompletionClient>(sp => sp.GetRequiredService<AzureFoundryChatCompletionClient>());

        services.AddScoped<IChatPipeline, ChatPipeline>();

        // --- Spec 006: multi-chat session persistence + parallel dispatch (depends on the above) ---
        services.AddScoped<IMultiChatSessionStore, CosmosMultiChatSessionStore>();
        services.AddScoped<MultiChatDispatcher>();

        return services;
    }
}
