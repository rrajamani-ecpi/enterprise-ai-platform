using EnterpriseAIPlatform.Domain.ModelAccess;

namespace EnterpriseAIPlatform.Application.ModelAccess;

/// <summary>
/// The last-resort defaults served when both the cache and the underlying config store are
/// unavailable (FR-014/SC-010). Documented, intentional fail-open — never a fabricated success
/// (Constitution Principle III).
/// </summary>
public static class HardcodedDefaults
{
    /// <summary>A single-role-open, single-model default so R1's one Azure/Foundry model stays reachable.</summary>
    public static SystemModelConfig SystemModelConfig => new()
    {
        RoleModelAccess = new Dictionary<string, List<string>>
        {
            ["default"] = new List<string> { "*" },
        },
        FallbackModelId = AzureFoundryDefaultModelId,
        UpdatedAtUtc = DateTimeOffset.MinValue,
    };

    /// <summary>The one R1-registered model id, shared with the Foundational seed data (T008/T037).</summary>
    public const string AzureFoundryDefaultModelId = "azure-foundry:gpt-5";
}
