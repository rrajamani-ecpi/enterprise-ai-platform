namespace EnterpriseAIPlatform.Infrastructure.Safety;

/// <summary>
/// Azure AI Content Safety endpoint configuration (spec 004 D7 — constitution Responsible AI
/// section). Deliberately has no API-key field: the guard authenticates via workload identity.
/// Validated at startup in <c>Program.cs</c> — unconfigured in Production fails app boot
/// (fail loud); unconfigured in Development is allowed so local dev doesn't require a live resource.
/// </summary>
public sealed class ContentSafetyOptions
{
    public const string SectionName = "ContentSafety";

    public string? Endpoint { get; init; }
}
