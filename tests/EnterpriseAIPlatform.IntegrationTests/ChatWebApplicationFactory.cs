using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Infrastructure.ModelAccess;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace EnterpriseAIPlatform.IntegrationTests;

/// <summary>
/// Spec 004 integration test host. Swaps <see cref="IChatThreadStore"/>/<see cref="IChatMessageStore"/>
/// (no live Cosmos) and <see cref="IChatCompletionClient"/> (no live Azure/Foundry) for in-memory
/// fakes; reuses <see cref="ModelAccessWebApplicationFactory"/>'s EF-InMemory + distributed-memory-cache
/// swap since the chat pipeline calls into spec 014's services too.
/// </summary>
public class ChatWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"chat-tests-{Guid.NewGuid()}";

    public FakeChatThreadStore ThreadStore { get; } = new();

    public FakeChatMessageStore MessageStore { get; } = new();

    public FakeChatCompletionClient CompletionClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            services.PostConfigureAll<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthHandler.SchemeName;
            });

            var efDescriptors = services
                .Where(d => d.ServiceType.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true)
                .ToList();
            foreach (var descriptor in efDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<ModelAccessDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();

            services.RemoveAll<IChatThreadStore>();
            services.AddSingleton<IChatThreadStore>(ThreadStore);

            services.RemoveAll<IChatMessageStore>();
            services.AddSingleton<IChatMessageStore>(MessageStore);

            services.RemoveAll<IChatCompletionClient>();
            services.AddSingleton<IChatCompletionClient>(CompletionClient);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ModelAccessDbContext>().Database.EnsureCreated();

        return host;
    }
}
