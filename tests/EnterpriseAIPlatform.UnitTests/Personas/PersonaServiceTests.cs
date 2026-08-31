using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.Personas;
using EnterpriseAIPlatform.Domain.Identity;
using EnterpriseAIPlatform.Domain.Personas;
using EnterpriseAIPlatform.Infrastructure.Identity;
using EnterpriseAIPlatform.Infrastructure.Personas;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseAIPlatform.UnitTests.Personas;

/// <summary>
/// Spec 009 US3 / FR-009/FR-010 / SC-006: <see cref="IPersonaRawAccessor.GetRawAsync"/> is the
/// sole path that can return a full <see cref="PersonaModel"/> (including <c>ApiKey</c>) — every
/// other <see cref="IPersonaService"/> method returns <see cref="PersonaPublicDTO"/> only.
/// </summary>
public class PersonaServiceTests
{
    private static PersonaDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PersonaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PersonaDbContext(options);
    }

    private static UserModel Admin() => new()
    {
        Name = "Admin",
        Email = "admin@contoso.com",
        Roles = new RoleFlags(true, false, false, false),
    };

    [Fact]
    public async Task GetRawAsync_ReturnsFullPersonaModel_IncludingApiKey()
    {
        using var db = CreateContext();
        var identityHasher = new IdentityHasher();
        var service = new PersonaService(db, identityHasher);

        var created = await service.CreateAsync(new PersonaModel
        {
            Id = string.Empty,
            OwnerUserId = string.Empty,
            OwnerPartitionKey = string.Empty,
            Model = "azure-foundry:gpt-5",
            Name = "A2A Persona",
        }, Admin());

        // ApiKey has no set-at-creation path in this build (FR-009/FR-010) -- set it directly to
        // prove GetRawAsync surfaces whatever is stored, unlike every IPersonaService method.
        var persona = await db.Personas.FirstAsync(p => p.Id == created.Response!.Id);
        persona.ApiKey = "secret-a2a-key";
        await db.SaveChangesAsync();

        var raw = await ((IPersonaRawAccessor)service).GetRawAsync(created.Response!.Id);

        Assert.True(raw.IsSuccess);
        Assert.Equal("secret-a2a-key", raw.Response!.ApiKey);
    }
}
