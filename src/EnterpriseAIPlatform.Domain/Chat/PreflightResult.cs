namespace EnterpriseAIPlatform.Domain.Chat;

/// <summary>Rejection reasons from <c>IChatPipeline</c>'s gate checks (spec 004 FR-001–FR-006).</summary>
public enum PreflightRejectionCode
{
    ThreadReadOnly,
    MessageTooLong,
    DailyLimitExceeded,
}

/// <summary>
/// The computed (never persisted) result of the message-limit/thread-version gate. When
/// <see cref="IsAllowed"/> is false, the caller MUST NOT have created or modified any
/// <see cref="ChatThreadModel"/>/<see cref="ChatMessageModel"/> document (FR-001).
/// </summary>
public sealed record PreflightResult
{
    public required bool IsAllowed { get; init; }

    public PreflightRejectionCode? RejectionCode { get; init; }

    /// <summary>Populated only for <see cref="PreflightRejectionCode.DailyLimitExceeded"/> (FR-004).</summary>
    public DateTimeOffset? ResetsAtUtc { get; init; }

    public static PreflightResult Allowed() => new() { IsAllowed = true };

    public static PreflightResult Rejected(PreflightRejectionCode code, DateTimeOffset? resetsAtUtc = null) =>
        new() { IsAllowed = false, RejectionCode = code, ResetsAtUtc = resetsAtUtc };
}
