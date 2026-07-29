using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Infrastructure.ModelAccess;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>Spec 014 US4 / FR-008 / SC-005: message-limit caps are re-validated server-side as integers &gt;= 1.</summary>
public class MessageLimitConfigServiceTests
{
    private static ModelAccessDbContext NewInMemoryDb() =>
        new(new DbContextOptionsBuilder<ModelAccessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SetAsync_Rejects_PerMessageCap_BelowOne(int invalidCap)
    {
        using var db = NewInMemoryDb();
        var service = new MessageLimitConfigService(db);

        var result = await service.SetAsync(invalidCap, null, "admin@contoso.com");

        Assert.Equal(ResponseStatus.ERROR, result.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task SetAsync_Rejects_DailyCap_BelowOne(int invalidCap)
    {
        using var db = NewInMemoryDb();
        var service = new MessageLimitConfigService(db);

        var result = await service.SetAsync(null, invalidCap, "admin@contoso.com");

        Assert.Equal(ResponseStatus.ERROR, result.Status);
    }

    [Fact]
    public async Task SetAsync_Accepts_ValidPositiveIntegerCaps()
    {
        using var db = NewInMemoryDb();
        var service = new MessageLimitConfigService(db);

        var result = await service.SetAsync(2000, 50, "admin@contoso.com");

        Assert.Equal(ResponseStatus.OK, result.Status);

        var read = await service.GetAsync();
        Assert.Equal(2000, read.Response!.PerMessageCharacterCap);
        Assert.Equal(50, read.Response.DailyMessageCap);
    }

    [Fact]
    public async Task SetAsync_RejectedWrite_LeavesPreviousCapInEffect()
    {
        using var db = NewInMemoryDb();
        var service = new MessageLimitConfigService(db);
        await service.SetAsync(1500, null, "admin@contoso.com");

        var rejected = await service.SetAsync(0, null, "admin@contoso.com");
        Assert.Equal(ResponseStatus.ERROR, rejected.Status);

        var read = await service.GetAsync();
        Assert.Equal(1500, read.Response!.PerMessageCharacterCap);
    }
}
