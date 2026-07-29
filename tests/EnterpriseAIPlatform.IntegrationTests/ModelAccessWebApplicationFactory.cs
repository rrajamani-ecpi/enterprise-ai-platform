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
/// Spec 014 integration test host. <see cref="CustomWebApplicationFactory"/> is sealed, so this
/// replicates its <see cref="TestAuthHandler"/> wiring directly, and additionally swaps
/// <see cref="ModelAccessDbContext"/> onto EF Core InMemory (a fresh, uniquely-named database per
/// factory instance — never shared across test classes) and the Redis-backed cache onto
/// <see cref="IDistributedCache"/>'s first-party in-memory implementation, so tests never require
/// a live Azure SQL/Redis dependency.
/// </summary>
public class ModelAccessWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"model-access-tests-{Guid.NewGuid()}";

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

            // Remove every EF-Core-related descriptor the production registration added
            // (UseSqlServer registers its provider services onto this same collection, not just
            // DbContextOptions<T> — removing only that descriptor leaves both providers
            // registered and EF throws "multiple database providers registered"). ModelAccessDbContext
            // is the only EF Core consumer in this app, so this is safe.
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
