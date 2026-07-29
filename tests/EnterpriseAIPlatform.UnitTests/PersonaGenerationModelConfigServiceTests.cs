using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Domain.ModelAccess;
using EnterpriseAIPlatform.Infrastructure.ModelAccess;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>Spec 014 US4 / FR-009 / SC-005, and the Edge Case requiring fail-closed on an empty/misconfigured allow-list.</summary>
public class PersonaGenerationModelConfigServiceTests
{
    private static ModelAccessDbContext NewInMemoryDb() =>
        new(new DbContextOptionsBuilder<ModelAccessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task SeedModel(ModelAccessDbContext db, string id)
    {
        db.ModelConfigs.Add(new ModelConfigDocument
        {
            Id = id,
            DisplayName = "Test",
            Provider = "test-provider",
            IsEnabled = true,
            SupportsToolCalling = true,
            SupportsVision = true,
            SupportsReasoning = true,
            AccessTier = ModelAccessTier.Standard,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SetAllowedModelsAsync_Rejects_ModelIdNotInGeneralRegistry()
    {
        using var db = NewInMemoryDb();
        var service = new PersonaGenerationModelConfigService(db);

        var result = await service.SetAllowedModelsAsync(new[] { "test:does-not-exist" }, "admin@contoso.com");

        Assert.Equal(ResponseStatus.ERROR, result.Status);
    }

    [Fact]
    public async Task SetAllowedModelsAsync_Accepts_ModelIdPresentInRegistry()
    {
        using var db = NewInMemoryDb();
        await SeedModel(db, "test:allowed-model");
        var service = new PersonaGenerationModelConfigService(db);

        var result = await service.SetAllowedModelsAsync(new[] { "test:allowed-model" }, "admin@contoso.com");

        Assert.Equal(ResponseStatus.OK, result.Status);
    }

    [Fact]
    public async Task ValidateSelectionAsync_Rejects_ModelOutsideAllowList_EvenIfInGeneralRegistry()
    {
        using var db = NewInMemoryDb();
        await SeedModel(db, "test:in-registry-not-allowed");
        await SeedModel(db, "test:on-allow-list");
        var service = new PersonaGenerationModelConfigService(db);
        await service.SetAllowedModelsAsync(new[] { "test:on-allow-list" }, "admin@contoso.com");

        var result = await service.ValidateSelectionAsync("test:in-registry-not-allowed");

        Assert.Equal(ResponseStatus.ERROR, result.Status);
    }

    [Fact]
    public async Task ValidateSelectionAsync_Accepts_ModelOnAllowList()
    {
        using var db = NewInMemoryDb();
        await SeedModel(db, "test:on-allow-list");
        var service = new PersonaGenerationModelConfigService(db);
        await service.SetAllowedModelsAsync(new[] { "test:on-allow-list" }, "admin@contoso.com");

        var result = await service.ValidateSelectionAsync("test:on-allow-list");

        Assert.Equal(ResponseStatus.OK, result.Status);
    }

    [Fact]
    public async Task ValidateSelectionAsync_FailsClosed_WhenAllowListIsEmpty()
    {
        using var db = NewInMemoryDb();
        var service = new PersonaGenerationModelConfigService(db);

        var result = await service.ValidateSelectionAsync("any-model");

        Assert.Equal(ResponseStatus.ERROR, result.Status);
        Assert.NotEmpty(result.Errors);
    }
}
