namespace EnterpriseAIPlatform.Application.Sharing;

/// <summary>
/// Platform-wide sharing overrides (spec 018 FR-007/FR-008/FR-009), evaluated ahead of per-role
/// policy (FR-010). Bound from the "GlobalSharingOverride" config section. Steady-state (no
/// override active) is all-<c>false</c>/empty.
/// </summary>
public sealed class GlobalSharingOverrideOptions
{
    public const string SectionName = "GlobalSharingOverride";

    public bool DisableAllGroupSharing { get; init; }

    public bool AdminOnlyMode { get; init; }

    public string[] GloballyAllowedGroups { get; init; } = Array.Empty<string>();
}
