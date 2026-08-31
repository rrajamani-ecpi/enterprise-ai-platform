using EnterpriseAIPlatform.Application.Personas;
using EnterpriseAIPlatform.Infrastructure.Personas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseAIPlatform.Infrastructure.DependencyInjection;

/// <summary>
/// Registers spec 009's persona CRUD + authorization services. Depends only on spec 002's
/// <c>AddPlatformInfrastructure</c> having already run (for <see cref="EnterpriseAIPlatform.Application.Identity.IIdentityHasher"/>).
/// </summary>
public static class PersonaServiceCollectionExtensions
{
    public static IServiceCollection AddPersonaInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Lazy connection string — the app boots without a live SQL dependency, mirroring spec
        // 014's ModelAccessDbContext registration.
        var sqlConnectionString = configuration["PersonaSql:ConnectionString"];
        services.AddDbContext<PersonaDbContext>(options =>
            options.UseSqlServer(string.IsNullOrWhiteSpace(sqlConnectionString) ? " " : sqlConnectionString));

        services.AddScoped<PersonaService>();
        services.AddScoped<IPersonaService>(sp => sp.GetRequiredService<PersonaService>());
        services.AddScoped<IPersonaRawAccessor>(sp => sp.GetRequiredService<PersonaService>());

        return services;
    }
}
