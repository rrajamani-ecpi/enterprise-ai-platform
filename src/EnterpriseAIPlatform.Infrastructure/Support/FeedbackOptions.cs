namespace EnterpriseAIPlatform.Infrastructure.Support;

/// <summary>
/// The external ECPI Feedback API's endpoint/credential (spec 017 D7). A third-party, non-Azure
/// system — a configured API key is the correct credential shape here (unlike the
/// workload-identity pattern used for Azure resources elsewhere in this codebase).
/// </summary>
public sealed class FeedbackOptions
{
    public const string SectionName = "Feedback";

    public string? EcpiApiEndpoint { get; init; }

    public string? EcpiApiKey { get; init; }
}
