namespace EnterpriseAIPlatform.Infrastructure.HealthChecks;

/// <summary>
/// Azure Key Vault endpoint configuration (spec 017 D6). Validated at startup in
/// <c>AddSupportInfrastructure</c>: unconfigured in Production fails app boot (fail loud);
/// unconfigured in Development is allowed — mirrors spec 004's <c>ContentSafetyOptions</c>.
/// </summary>
public sealed class KeyVaultOptions
{
    public const string SectionName = "KeyVault";

    public string? VaultUri { get; init; }
}
