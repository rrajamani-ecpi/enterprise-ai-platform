namespace EnterpriseAIPlatform.Domain.ModelAccess;

/// <summary>
/// Message-limit policy (spec 014 Key Entities). Read-open to any authenticated caller (FR-004);
/// write admin-gated (FR-006) and server-revalidated as an integer &gt;= 1 on every write (FR-008).
/// Both caps are independently toggleable — <c>null</c> means disabled.
/// </summary>
public sealed class MessageLimitConfig
{
    /// <summary>Singleton row id — always 1.</summary>
    public int Id { get; set; } = 1;

    public int? PerMessageCharacterCap { get; set; }

    public int? DailyMessageCap { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public string? UpdatedByUserId { get; set; }
}
