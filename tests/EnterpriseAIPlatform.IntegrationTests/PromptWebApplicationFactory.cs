using EnterpriseAIPlatform.Infrastructure.Prompts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EnterpriseAIPlatform.IntegrationTests;

/// <summary>
/// Spec 016 integration test host, following <see cref="PersonaWebApplicationFactory"/>'s pattern
/// exactly: replicates the <see cref="TestAuthHandler"/> wiring and swaps
/// <see cref="PromptDbContext"/> onto EF Core InMemory with a fresh, uniquely-named database per
/// factory instance, so tests never require a live Azure SQL dependency.
/// </summary>
public class PromptWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"prompt-tests-{Guid.NewGuid()}";

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

            // Remove only PromptDbContext's own descriptors — never a blanket EF-Core-namespace
            // sweep, which would also strip PersonaDbContext's and ModelAccessDbContext's
            // registrations and fail Development-environment eager DI validation (ValidateOnBuild)
            // for every dependent service, not just this feature's (research.md D9).
            var promptDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<PromptDbContext>) || d.ServiceType == typeof(PromptDbContext))
                .ToList();
            foreach (var descriptor in promptDescriptors)
            {
                services.Remove(descriptor);
            }

            // EF Core's default internal service provider is scanned per-app, not per-DbContext —
            // the sibling contexts' SqlServer provider services (untouched, still needed in this
            // same container) would otherwise collide with InMemory's, throwing "multiple database
            // providers registered." An isolated internal service provider scoped to just this
            // DbContext is EF's documented fix for exactly this scenario.
            var promptInternalServices = new ServiceCollection().AddEntityFrameworkInMemoryDatabase().BuildServiceProvider();
            services.AddDbContext<PromptDbContext>(options => options
                .UseInMemoryDatabase(_databaseName)
                .UseInternalServiceProvider(promptInternalServices));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<PromptDbContext>().Database.EnsureCreated();

        return host;
    }
}
