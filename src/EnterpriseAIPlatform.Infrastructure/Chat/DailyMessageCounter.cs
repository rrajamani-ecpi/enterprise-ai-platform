using EnterpriseAIPlatform.Application.Chat;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace EnterpriseAIPlatform.Infrastructure.Chat;

/// <summary>
/// The single implementation of <see cref="IDailyMessageCounter"/>. Backed by the Redis-based
/// <see cref="IDistributedCache"/> spec 014 already registers.
/// </summary>
public sealed class DailyMessageCounter : IDailyMessageCounter
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<DailyMessageCounter> _logger;
    private static readonly TimeZoneInfo EasternTime = ResolveEasternTimeZone();

    public DailyMessageCounter(IDistributedCache cache, ILogger<DailyMessageCounter> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Returns null (unreadable) rather than throwing — the caller fails open on null.</summary>
    public async Task<int?> GetCountAsync(string ownerPartitionKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var raw = await _cache.GetStringAsync(KeyFor(ownerPartitionKey), cancellationToken);
            return raw is null ? 0 : int.Parse(raw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Daily message counter read failed for a caller; treated as unreadable (fail-open).");
            return null;
        }
    }

    public async Task IncrementAsync(string ownerPartitionKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var current = await GetCountAsync(ownerPartitionKey, cancellationToken) ?? 0;
            await _cache.SetStringAsync(
                KeyFor(ownerPartitionKey),
                (current + 1).ToString(),
                new DistributedCacheEntryOptions { AbsoluteExpiration = NextResetUtc() },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Daily message counter increment failed for a caller; the count may undercount.");
        }
    }

    /// <summary>Midnight America/New_York, per FR-004.</summary>
    public DateTimeOffset NextResetUtc()
    {
        var nowEastern = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, EasternTime);
        var nextMidnightEastern = new DateTimeOffset(nowEastern.Date.AddDays(1), nowEastern.Offset);
        return TimeZoneInfo.ConvertTime(nextMidnightEastern, TimeZoneInfo.Utc);
    }

    private static string KeyFor(string ownerPartitionKey) =>
        $"eap:chat:daily-count:{ownerPartitionKey}:{DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, EasternTime).Date):yyyy-MM-dd}";

    private static TimeZoneInfo ResolveEasternTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }
}
