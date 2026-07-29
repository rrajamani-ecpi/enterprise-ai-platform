namespace EnterpriseAIPlatform.Infrastructure.ModelProviders;

/// <summary>Non-secret provider endpoint configuration. Deliberately has no API-key/secret field (FR-013).</summary>
public sealed class AzureFoundryOptions
{
    public const string SectionName = "ModelProviders:AzureFoundry";

    public string? Endpoint { get; init; }
}
