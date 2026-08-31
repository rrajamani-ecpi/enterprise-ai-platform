namespace EnterpriseAIPlatform.Domain.Sharing;

/// <summary>
/// Why a <see cref="SharingDecision"/> resolved the way it did — exists purely for testability/
/// observability (spec 018 data-model.md), not part of any persisted record.
/// </summary>
public enum SharingDecisionReason
{
    AdminOnlyModeActive,
    AdminBypass,
    GroupSharingDisabledGlobally,
    GloballyAllowedGroup,
    RolePolicyAllowed,
    RolePolicyDenied,
}

/// <summary>
/// The server-side allow/deny result of evaluating a <see cref="ShareTargetRequest"/> (spec 018
/// FR-002/FR-015) — the canonical decision every resource-type spec (009/012/016) should consume
/// rather than reimplement.
/// </summary>
public sealed record SharingDecision(bool IsAllowed, SharingDecisionReason Reason);
