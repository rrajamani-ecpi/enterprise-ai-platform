using System.Text.Json;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.ModelAccess;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace EnterpriseAIPlatform.Infrastructure.ModelAccess;

/// <summary>
/// The single implementation of the three-tier fallback (FR-014/SC-010): Redis cache -> SQL store
/// -> hardcoded defaults. <see cref="GetAsync"/> never throws — a cache or store outage degrades
/// to defaults instead of failing the dependent request (Constitution Principle III).
/// </summary>
public sealed class RedisSystemModelConfigCache : ISystemModelConfigCache
{
    private const string CacheKey = "eap:model-access:system-config";
    private static readonly DistributedCacheEntryOptions CacheEntryOptions =
        new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };

    private readonly IDistributedCache _cache;
    private readonly ISystemModelConfigStore _store;
    private readonly ILogger<RedisSystemModelConfigCache> _logger;

    public RedisSystemModelConfigCache(
        IDistributedCache cache, ISystemModelConfigStore store, ILogger<RedisSystemModelConfigCache> logger)
    {
        _cache = cache;
        _store = store;
        _logger = logger;
    }

    public async Task<SystemModelConfig> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var cached = await _cache.GetStringAsync(CacheKey, cancellationToken);
            if (cached is not null)
            {
                var deserialized = JsonSerializer.Deserialize<SystemModelConfig>(cached);
                if (deserialized is not null)
                {
                    return deserialized;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SystemModelConfig cache read failed; falling back to the SQL store.");
        }

        try
        {
            var fromStore = await _store.GetAsync(cancellationToken);
            await TrySetCacheAsync(fromStore, cancellationToken);
            return fromStore;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SystemModelConfig store read failed; falling back to hardcoded defaults.");
            return HardcodedDefaults.SystemModelConfig;
        }
    }

    public async Task InvalidateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.RemoveAsync(CacheKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SystemModelConfig cache invalidation failed; next read may serve a stale value.");
        }
    }

    private async Task TrySetCacheAsync(SystemModelConfig config, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.SetStringAsync(CacheKey, JsonSerializer.Serialize(config), CacheEntryOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SystemModelConfig cache write failed; the read still succeeded from the store.");
        }
    }
}
