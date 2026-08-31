using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EnterpriseAIPlatform.Infrastructure.Prompts;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations add</c>/<c>database update</c> can construct
/// <see cref="PromptDbContext"/> without booting the full Web host. Never used at runtime — the
/// real app registers the context via DI in <c>Program.cs</c>.
/// </summary>
public sealed class PromptDbContextFactory : IDesignTimeDbContextFactory<PromptDbContext>
{
    public PromptDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PROMPT_SQL_CONNECTION_STRING")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=EnterpriseAIPlatform.Prompts;Trusted_Connection=True;";

        var options = new DbContextOptionsBuilder<PromptDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new PromptDbContext(options);
    }
}
