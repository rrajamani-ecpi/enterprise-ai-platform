using EnterpriseAIPlatform.Application.Authorization;
using EnterpriseAIPlatform.Domain.Identity;
using EnterpriseAIPlatform.Domain.Sharing;

namespace EnterpriseAIPlatform.Application.Sharing;

/// <summary>
/// The single implementation of spec 018's sharing-policy decision (Constitution Principle IV) —
/// framework-free so the full role x target-type x override matrix (SC-002 through SC-005) is
/// unit-testable without any infrastructure, mirroring spec 014's <c>ModelAccessEvaluator</c>.
/// </summary>
public static class SharingPolicyEvaluator
{
    /// <summary>
    /// Evaluates a single share-target request against the caller's roles, the configured
    /// per-role policy, and any active global override, per the ordered precedence rule in
    /// research.md D5: AdminOnlyMode → admin bypass → DisableAllGroupSharing →
    /// GloballyAllowedGroups → per-role policy.
    /// </summary>
    public static SharingDecision Evaluate(
        RoleFlags callerRoles,
        ShareTargetRequest request,
        RoleSharingPolicyOptions rolePolicy,
        GlobalSharingOverrideOptions globalOverride)
    {
        if (globalOverride.AdminOnlyMode && !callerRoles.IsAdmin)
        {
            return new SharingDecision(false, SharingDecisionReason.AdminOnlyModeActive);
        }

        if (request.Type == ShareTargetType.Individual)
        {
            // FR-004/FR-005: individual sharing is always allowed for non-admin roles; the
            // AdminOnlyMode check above is the only thing that can deny it (research.md D3).
            return new SharingDecision(
                true,
                callerRoles.IsAdmin ? SharingDecisionReason.AdminBypass : SharingDecisionReason.RolePolicyAllowed);
        }

        // From here, request.Type == ShareTargetType.Group.
        if (callerRoles.IsAdmin)
        {
            return new SharingDecision(true, SharingDecisionReason.AdminBypass);
        }

        if (globalOverride.DisableAllGroupSharing)
        {
            return new SharingDecision(false, SharingDecisionReason.GroupSharingDisabledGlobally);
        }

        if (request.GroupToken is not null && globalOverride.GloballyAllowedGroups.Contains(request.GroupToken))
        {
            return new SharingDecision(true, SharingDecisionReason.GloballyAllowedGroup);
        }

        foreach (var role in RoleNamesFor(callerRoles))
        {
            var policy = rolePolicy.Roles.GetValueOrDefault(role, RolePolicy.Default);
            if (policy.GroupSharingEnabled && request.GroupToken is not null && policy.AllowedGroups.Contains(request.GroupToken))
            {
                return new SharingDecision(true, SharingDecisionReason.RolePolicyAllowed);
            }
        }

        return new SharingDecision(false, SharingDecisionReason.RolePolicyDenied);
    }

    /// <summary>
    /// A caller may hold multiple simultaneous role flags (spec 002 — "independent, not mutually
    /// exclusive"); the most-permissive-applicable-role wins (spec 018 Edge Cases), which the
    /// "any role allows it" loop in <see cref="Evaluate"/> implements directly.
    /// </summary>
    private static IEnumerable<RoleName> RoleNamesFor(RoleFlags roles)
    {
        if (roles.IsEmployee)
        {
            yield return RoleName.Employee;
        }

        if (roles.IsContractor)
        {
            yield return RoleName.Contractor;
        }

        if (roles.IsStudent)
        {
            yield return RoleName.Student;
        }
    }
}
