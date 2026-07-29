namespace EnterpriseAIPlatform.Domain.Chat;

/// <summary>The computed (never persisted) result of a Content Safety check (constitution Responsible AI section).</summary>
public sealed record ContentSafetyVerdict(bool IsAllowed, string? Category = null)
{
    public static ContentSafetyVerdict Allowed() => new(true);

    public static ContentSafetyVerdict Blocked(string category) => new(false, category);
}
