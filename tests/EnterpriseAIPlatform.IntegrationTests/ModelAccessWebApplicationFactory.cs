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

            // Remove only ModelAccessDbContext's own descriptors — a blanket removal of every
            // EF-Core-namespaced descriptor would also strip spec 009's PersonaDbContext
            // registration, which the ASP.NET Core Development-environment eager DI validation
            // (ValidateOnBuild) then fails on for every Persona-dependent service, even though
            // this factory never touches Personas.
            var modelAccessDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<ModelAccessDbContext>) || d.ServiceType == typeof(ModelAccessDbContext))
                .ToList();
            foreach (var descriptor in modelAccessDescriptors)
            {
                services.Remove(descriptor);
            }

            // Isolated internal service provider (see PersonaWebApplicationFactory for the same
            // pattern, needed the same way now that spec 009's PersonaDbContext (SqlServer)
            // coexists in this container) — EF Core's default internal service provider is scanned
            // per-app, not per-DbContext, so without this, InMemory's provider services here would
            // collide with PersonaDbContext's untouched SqlServer registration.
            var modelAccessInternalServices = new ServiceCollection().AddEntityFrameworkInMemoryDatabase().BuildServiceProvider();
            services.AddDbContext<ModelAccessDbContext>(options => options
                .UseInMemoryDatabase(_databaseName)
                .UseInternalServiceProvider(modelAccessInternalServices));

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
