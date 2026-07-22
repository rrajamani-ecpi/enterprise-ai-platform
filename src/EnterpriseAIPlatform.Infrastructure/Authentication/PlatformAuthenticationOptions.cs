namespace EnterpriseAIPlatform.Infrastructure.Authentication;

/// <summary>
/// Selects the identity provider used by the web host. Development mode is intended only for
/// local UI development; deployed environments must use Microsoft Entra ID.
/// </summary>
public sealed class PlatformAuthenticationOptions
{
    public const string SectionName = "PlatformAuthentication";

    public PlatformAuthenticationMode Mode { get; set; } = PlatformAuthenticationMode.Entra;

    public DevelopmentUserOptions DevelopmentUser { get; set; } = new();
}

public enum PlatformAuthenticationMode
{
    Entra,
    Development,
}

/// <summary>The simulated user exposed when development authentication is enabled.</summary>
public sealed class DevelopmentUserOptions
{
    public string Name { get; set; } = "Local Developer";
    public string Email { get; set; } = "developer@localhost";
    public bool IsAdmin { get; set; } = true;
    public bool IsEmployee { get; set; } = true;
    public bool IsContractor { get; set; }
    public bool IsStudent { get; set; }
    public bool AdvancedModelAccess { get; set; } = true;
}
