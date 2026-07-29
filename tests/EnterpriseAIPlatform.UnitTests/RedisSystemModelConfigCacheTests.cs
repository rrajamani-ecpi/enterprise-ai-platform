using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.ModelAccess;
using EnterpriseAIPlatform.Infrastructure.ModelAccess;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>
/// Spec 014 FR-014 / SC-010: Redis cache -> SQL store -> hardcoded defaults. <see cref="ISystemModelConfigCache.GetAsync"/>
/// must never throw, degrading gracefully at each tier (Constitution Principle III's documented fail-open carve-out).
/// </summary>
public class RedisSystemModelConfigCacheTests
{
    private static SystemModelConfig StoreConfig() => new()
    {
        RoleModelAccess = new Dictionary<string, List<string>> { ["default"] = new List<string> { "*" } },
        FallbackModelId = "azure-foundry:gpt-5",
    };

    [Fact]
    public async Task GetAsync_CacheUnavailable_FallsThroughToStore()
    {
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<byte[]?>>(_ => throw new InvalidOperationException("Redis unavailable"));

        var store = Substitute.For<ISystemModelConfigStore>();
        store.GetAsync(Arg.Any<CancellationToken>()).Returns(StoreConfig());

        var sut = new RedisSystemModelConfigCache(cache, store, NullLogger<RedisSystemModelConfigCache>.Instance);

        var result = await sut.GetAsync();

        Assert.Equal("azure-foundry:gpt-5", result.FallbackModelId);
    }

    [Fact]
    public async Task GetAsync_CacheAndStoreBothUnavailable_FallsBackToHardcodedDefaults_NeverThrows()
    {
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<byte[]?>>(_ => throw new InvalidOperationException("Redis unavailable"));

        var store = Substitute.For<ISystemModelConfigStore>();
        store.GetAsync(Arg.Any<CancellationToken>())
            .Returns<Task<SystemModelConfig>>(_ => throw new InvalidOperationException("SQL unavailable"));

        var sut = new RedisSystemModelConfigCache(cache, store, NullLogger<RedisSystemModelConfigCache>.Instance);

        var result = await sut.GetAsync();

        Assert.Equal(HardcodedDefaults.AzureFoundryDefaultModelId, result.FallbackModelId);
    }

    [Fact]
    public async Task InvalidateAsync_CacheUnavailable_DoesNotThrow()
    {
        var cache = Substitute.For<IDistributedCache>();
        cache.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("Redis unavailable"));
        var store = Substitute.For<ISystemModelConfigStore>();

        var sut = new RedisSystemModelConfigCache(cache, store, NullLogger<RedisSystemModelConfigCache>.Instance);

        await sut.InvalidateAsync(); // must not throw
    }
}
