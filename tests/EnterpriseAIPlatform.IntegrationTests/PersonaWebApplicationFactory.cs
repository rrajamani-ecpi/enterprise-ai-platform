using EnterpriseAIPlatform.Infrastructure.Personas;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EnterpriseAIPlatform.IntegrationTests;

/// <summary>
/// Spec 009 integration test host. <see cref="CustomWebApplicationFactory"/> is sealed, so this
/// replicates its <see cref="TestAuthHandler"/> wiring directly, and additionally swaps
/// <see cref="PersonaDbContext"/> onto EF Core InMemory (a fresh, uniquely-named database per
/// factory instance — never shared across test classes), so tests never require a live Azure SQL
/// dependency. Removes only <see cref="PersonaDbContext"/>'s own descriptors — a blanket removal
/// of every EF-Core-namespaced descriptor (as an earlier version of this factory, and
/// <see cref="ModelAccessWebApplicationFactory"/>, once did) also strips <c>ModelAccessDbContext</c>'s
/// registration, which the ASP.NET Core Development-environment eager DI validation
/// (<c>ValidateOnBuild</c>) then fails on for every ModelAccess-dependent service, not just Persona's.
/// </summary>
public class PersonaWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"persona-tests-{Guid.NewGuid()}";

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

            // Remove only PersonaDbContext's own descriptors (its SqlServer-configured
            // DbContextOptions<PersonaDbContext> and the context type itself) — never a blanket
            // EF-Core-namespace sweep, which would also strip ModelAccessDbContext's registration.
            var personaDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<PersonaDbContext>) || d.ServiceType == typeof(PersonaDbContext))
                .ToList();
            foreach (var descriptor in personaDescriptors)
            {
                services.Remove(descriptor);
            }

            // EF Core's default internal service provider is scanned per-app, not per-DbContext —
            // ModelAccessDbContext's SqlServer provider services (untouched, still needed by
            // ModelAccess-dependent services in this same container) would otherwise collide with
            // InMemory's, throwing "multiple database providers registered." An isolated internal
            // service provider, scoped to just this DbContext, is EF's documented fix for exactly
            // this multiple-provider-per-app scenario.
            var personaInternalServices = new ServiceCollection().AddEntityFrameworkInMemoryDatabase().BuildServiceProvider();
            services.AddDbContext<PersonaDbContext>(options => options
                .UseInMemoryDatabase(_databaseName)
                .UseInternalServiceProvider(personaInternalServices));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<PersonaDbContext>().Database.EnsureCreated();

        return host;
    }
}
