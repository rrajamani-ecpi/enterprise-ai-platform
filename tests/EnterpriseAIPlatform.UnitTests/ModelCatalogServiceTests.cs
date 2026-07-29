using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Domain.ModelAccess;
using EnterpriseAIPlatform.Infrastructure.ModelAccess;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>Spec 014 US2 / FR-002 / SC-003, and the Edge Case requiring explicit capability flags.</summary>
public class ModelCatalogServiceTests
{
    private static ModelAccessDbContext NewInMemoryDb() =>
        new(new DbContextOptionsBuilder<ModelAccessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ModelConfigDocument NewModel(string id, bool? toolCalling = true, bool? vision = true, bool? reasoning = true) => new()
    {
        Id = id,
        DisplayName = "Test",
        Provider = "test-provider",
        IsEnabled = true,
        SupportsToolCalling = toolCalling,
        SupportsVision = vision,
        SupportsReasoning = reasoning,
        AccessTier = ModelAccessTier.Standard,
    };

    [Fact]
    public async Task SoftDeleteAsync_NeverPhysicallyRemoves_TheUnderlyingRow()
    {
        using var db = NewInMemoryDb();
        var service = new ModelCatalogService(db);
        await service.UpsertAsync(NewModel("test:soft-delete"));

        var deleteResult = await service.SoftDeleteAsync("test:soft-delete");
        Assert.Equal(ResponseStatus.OK, deleteResult.Status);

        var excludedByDefault = await service.GetAsync("test:soft-delete");
        Assert.Equal(ResponseStatus.NOT_FOUND, excludedByDefault.Status);

        var includingDeleted = await service.GetAsync("test:soft-delete", includeDeleted: true);
        Assert.Equal(ResponseStatus.OK, includingDeleted.Status);
        Assert.True(includingDeleted.Response!.IsDeleted);
    }

    [Theory]
    [InlineData(null, true, true)]
    [InlineData(true, null, true)]
    [InlineData(true, true, null)]
    public async Task UpsertAsync_Rejects_AnyUnsetCapabilityFlag(bool? toolCalling, bool? vision, bool? reasoning)
    {
        using var db = NewInMemoryDb();
        var service = new ModelCatalogService(db);

        var result = await service.UpsertAsync(NewModel("test:incomplete", toolCalling, vision, reasoning));

        Assert.Equal(ResponseStatus.ERROR, result.Status);
    }

    [Fact]
    public async Task UpsertAsync_Accepts_WhenAllCapabilityFlagsAreExplicitlySet()
    {
        using var db = NewInMemoryDb();
        var service = new ModelCatalogService(db);

        var result = await service.UpsertAsync(NewModel("test:complete"));

        Assert.Equal(ResponseStatus.OK, result.Status);
    }
}
