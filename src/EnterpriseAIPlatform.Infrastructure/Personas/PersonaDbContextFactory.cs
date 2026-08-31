using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EnterpriseAIPlatform.Infrastructure.Personas;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations add</c>/<c>database update</c> can construct
/// <see cref="PersonaDbContext"/> without booting the full Web host. Never used at runtime — the
/// real app registers the context via DI in <c>Program.cs</c>.
/// </summary>
public sealed class PersonaDbContextFactory : IDesignTimeDbContextFactory<PersonaDbContext>
{
    public PersonaDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PERSONA_SQL_CONNECTION_STRING")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=EnterpriseAIPlatform.Personas;Trusted_Connection=True;";

        var options = new DbContextOptionsBuilder<PersonaDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new PersonaDbContext(options);
    }
}
