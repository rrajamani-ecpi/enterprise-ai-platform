namespace EnterpriseAIPlatform.Application.Chat;

/// <summary>
/// Backs the daily message cap (spec 004 FR-004/FR-005). Both methods fail open (return/no-op
/// rather than throw) on a store read/write failure — the preflight check, not this type, decides
/// whether that unreadability means "allow" (FR-005).
/// </summary>
public interface IDailyMessageCounter
{
    /// <summary>Returns null when the count is unreadable — the caller fails open on null.</summary>
    Task<int?> GetCountAsync(string ownerPartitionKey, CancellationToken cancellationToken = default);

    Task IncrementAsync(string ownerPartitionKey, CancellationToken cancellationToken = default);

    /// <summary>Midnight America/New_York, per FR-004.</summary>
    DateTimeOffset NextResetUtc();
}
