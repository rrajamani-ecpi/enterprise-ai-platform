using EnterpriseAIPlatform.Application.Authorization;
using EnterpriseAIPlatform.Application.Sharing;
using EnterpriseAIPlatform.Domain.Identity;
using EnterpriseAIPlatform.Domain.Sharing;

namespace EnterpriseAIPlatform.UnitTests.Sharing;

/// <summary>
/// Spec 018 US1-US3 / FR-002–FR-010 / SC-002–SC-005: the role x target-type x override decision
/// matrix produced by <see cref="SharingPolicyEvaluator"/>.
/// </summary>
public class SharingPolicyEvaluatorTests
{
    private static readonly RoleSharingPolicyOptions DefaultRolePolicy = new()
    {
        Roles = new Dictionary<RoleName, RolePolicy>
        {
            [RoleName.Employee] = new() { GroupSharingEnabled = true, AllowedGroups = new[] { "faculty", "students" } },
            [RoleName.Student] = new() { GroupSharingEnabled = true, AllowedGroups = new[] { "students" } },
            // Contractor intentionally omitted -- spec 018 defines no policy for it; falls back to RolePolicy.Default.
        },
    };

    private static readonly GlobalSharingOverrideOptions NoOverride = new();

    private static ShareTargetRequest Individual() => new(ShareTargetType.Individual);

    private static ShareTargetRequest Group(string token) => new(ShareTargetType.Group, token);

    // --- US1: baseline vocabulary (individual always allowed; group per role policy) ---

    [Theory]
    [InlineData(false, true, false, false)]  // Employee
    [InlineData(false, false, false, true)]   // Student
    [InlineData(false, false, true, false)]   // Contractor (unconfigured -> fail-safe default; individual still always allowed)
    public void Individual_AlwaysAllowed_ForAnyNonAdminRole_WithNoOverride(
        bool isAdmin, bool isEmployee, bool isContractor, bool isStudent)
    {
        var caller = new RoleFlags(isAdmin, isEmployee, isContractor, isStudent);

        var decision = SharingPolicyEvaluator.Evaluate(caller, Individual(), DefaultRolePolicy, NoOverride);

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void Group_Allowed_WhenGroupInCallersAllowedGroups()
    {
        var employee = new RoleFlags(false, true, false, false);

        var decision = SharingPolicyEvaluator.Evaluate(employee, Group("faculty"), DefaultRolePolicy, NoOverride);

        Assert.True(decision.IsAllowed);
        Assert.Equal(SharingDecisionReason.RolePolicyAllowed, decision.Reason);
    }

    [Fact]
    public void Group_Denied_WhenGroupNotInCallersAllowedGroups()
    {
        var student = new RoleFlags(false, false, false, true); // AllowedGroups = ["students"]

        var decision = SharingPolicyEvaluator.Evaluate(student, Group("faculty"), DefaultRolePolicy, NoOverride);

        Assert.False(decision.IsAllowed);
        Assert.Equal(SharingDecisionReason.RolePolicyDenied, decision.Reason);
    }

    [Fact]
    public void Group_Denied_ForContractor_UnconfiguredRoleFallsBackToRestrictiveDefault()
    {
        var contractor = new RoleFlags(false, false, true, false);

        var decision = SharingPolicyEvaluator.Evaluate(contractor, Group("students"), DefaultRolePolicy, NoOverride);

        Assert.False(decision.IsAllowed);
        Assert.Equal(SharingDecisionReason.RolePolicyDenied, decision.Reason);
    }

    // --- US2: admin bypass + full role x target-type matrix (SC-002) ---

    [Theory]
    [InlineData("faculty")]
    [InlineData("students")]
    [InlineData("anything-not-configured")]
    public void Admin_AlwaysAllowed_ForAnyGroup_RegardlessOfConfig(string group)
    {
        var admin = new RoleFlags(true, false, false, false);

        var decision = SharingPolicyEvaluator.Evaluate(admin, Group(group), DefaultRolePolicy, NoOverride);

        Assert.True(decision.IsAllowed);
        Assert.Equal(SharingDecisionReason.AdminBypass, decision.Reason);
    }

    [Fact]
    public void Admin_AlwaysAllowed_ForIndividual()
    {
        var admin = new RoleFlags(true, false, false, false);

        var decision = SharingPolicyEvaluator.Evaluate(admin, Individual(), DefaultRolePolicy, NoOverride);

        Assert.True(decision.IsAllowed);
    }

    [Theory]
    [InlineData(false, true, false, false, "faculty", true)]
    [InlineData(false, true, false, false, "students", true)]
    [InlineData(false, true, false, false, "unlisted", false)]
    [InlineData(false, false, false, true, "students", true)]
    [InlineData(false, false, false, true, "faculty", false)]
    [InlineData(false, false, true, false, "students", false)] // Contractor unconfigured -> denied
    [InlineData(true, false, false, false, "unlisted", true)]  // Admin -> allowed regardless
    public void FullMatrix_MatchesConfiguredPolicy(
        bool isAdmin, bool isEmployee, bool isContractor, bool isStudent, string group, bool expectedAllowed)
    {
        var caller = new RoleFlags(isAdmin, isEmployee, isContractor, isStudent);

        var decision = SharingPolicyEvaluator.Evaluate(caller, Group(group), DefaultRolePolicy, NoOverride);

        Assert.Equal(expectedAllowed, decision.IsAllowed);
    }

    // --- US3: global overrides (SC-003, SC-004, SC-005) ---

    [Fact]
    public void DisableAllGroupSharing_DeniesNonAdminGroupRequest()
    {
        var employee = new RoleFlags(false, true, false, false);
        var globalOverride = new GlobalSharingOverrideOptions { DisableAllGroupSharing = true };

        var decision = SharingPolicyEvaluator.Evaluate(employee, Group("faculty"), DefaultRolePolicy, globalOverride);

        Assert.False(decision.IsAllowed);
        Assert.Equal(SharingDecisionReason.GroupSharingDisabledGlobally, decision.Reason);
    }

    [Fact]
    public void DisableAllGroupSharing_LeavesIndividualSharingUnaffected()
    {
        var employee = new RoleFlags(false, true, false, false);
        var globalOverride = new GlobalSharingOverrideOptions { DisableAllGroupSharing = true };

        var decision = SharingPolicyEvaluator.Evaluate(employee, Individual(), DefaultRolePolicy, globalOverride);

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void DisableAllGroupSharing_LeavesAdminUnaffected()
    {
        var admin = new RoleFlags(true, false, false, false);
        var globalOverride = new GlobalSharingOverrideOptions { DisableAllGroupSharing = true };

        var decision = SharingPolicyEvaluator.Evaluate(admin, Group("faculty"), DefaultRolePolicy, globalOverride);

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void AdminOnlyMode_DeniesNonAdmin_ForIndividualAndGroup()
    {
        var employee = new RoleFlags(false, true, false, false);
        var globalOverride = new GlobalSharingOverrideOptions { AdminOnlyMode = true };

        var individualDecision = SharingPolicyEvaluator.Evaluate(employee, Individual(), DefaultRolePolicy, globalOverride);
        var groupDecision = SharingPolicyEvaluator.Evaluate(employee, Group("faculty"), DefaultRolePolicy, globalOverride);

        Assert.False(individualDecision.IsAllowed);
        Assert.Equal(SharingDecisionReason.AdminOnlyModeActive, individualDecision.Reason);
        Assert.False(groupDecision.IsAllowed);
        Assert.Equal(SharingDecisionReason.AdminOnlyModeActive, groupDecision.Reason);
    }

    [Fact]
    public void AdminOnlyMode_LeavesAdminUnaffected()
    {
        var admin = new RoleFlags(true, false, false, false);
        var globalOverride = new GlobalSharingOverrideOptions { AdminOnlyMode = true };

        var individualDecision = SharingPolicyEvaluator.Evaluate(admin, Individual(), DefaultRolePolicy, globalOverride);
        var groupDecision = SharingPolicyEvaluator.Evaluate(admin, Group("faculty"), DefaultRolePolicy, globalOverride);

        Assert.True(individualDecision.IsAllowed);
        Assert.True(groupDecision.IsAllowed);
    }

    [Fact]
    public void GloballyAllowedGroups_AllowsGroup_EvenWhenRolePolicyWouldDeny()
    {
        var student = new RoleFlags(false, false, false, true); // AllowedGroups = ["students"] only
        var globalOverride = new GlobalSharingOverrideOptions { GloballyAllowedGroups = new[] { "announcements" } };

        var decision = SharingPolicyEvaluator.Evaluate(student, Group("announcements"), DefaultRolePolicy, globalOverride);

        Assert.True(decision.IsAllowed);
        Assert.Equal(SharingDecisionReason.GloballyAllowedGroup, decision.Reason);
    }

    [Fact]
    public void GloballyAllowedGroups_YieldsToAdminOnlyMode()
    {
        var student = new RoleFlags(false, false, false, true);
        var globalOverride = new GlobalSharingOverrideOptions
        {
            AdminOnlyMode = true,
            GloballyAllowedGroups = new[] { "announcements" },
        };

        var decision = SharingPolicyEvaluator.Evaluate(student, Group("announcements"), DefaultRolePolicy, globalOverride);

        Assert.False(decision.IsAllowed);
        Assert.Equal(SharingDecisionReason.AdminOnlyModeActive, decision.Reason);
    }

    [Fact]
    public void GloballyAllowedGroups_YieldsToDisableAllGroupSharing()
    {
        var student = new RoleFlags(false, false, false, true);
        var globalOverride = new GlobalSharingOverrideOptions
        {
            DisableAllGroupSharing = true,
            GloballyAllowedGroups = new[] { "announcements" },
        };

        var decision = SharingPolicyEvaluator.Evaluate(student, Group("announcements"), DefaultRolePolicy, globalOverride);

        Assert.False(decision.IsAllowed);
        Assert.Equal(SharingDecisionReason.GroupSharingDisabledGlobally, decision.Reason);
    }

    [Fact]
    public void DeactivatedOverride_RestoresPerRolePolicy_WithNoResidualEffect()
    {
        var student = new RoleFlags(false, false, false, true);

        // Simulates an override having been active, then deactivated: evaluate with a fresh,
        // all-false override instance and confirm only the per-role policy governs.
        var decision = SharingPolicyEvaluator.Evaluate(student, Group("faculty"), DefaultRolePolicy, new GlobalSharingOverrideOptions());

        Assert.False(decision.IsAllowed); // "faculty" isn't in Student's AllowedGroups
        Assert.Equal(SharingDecisionReason.RolePolicyDenied, decision.Reason);
    }

    // --- Multi-role resolution (spec Edge Cases: most permissive applicable role wins) ---

    [Fact]
    public void MultiRole_MostPermissiveRoleWins()
    {
        var rolePolicy = new RoleSharingPolicyOptions
        {
            Roles = new Dictionary<RoleName, RolePolicy>
            {
                [RoleName.Employee] = new() { GroupSharingEnabled = true, AllowedGroups = new[] { "faculty" } },
                [RoleName.Student] = new() { GroupSharingEnabled = true, AllowedGroups = new[] { "students" } },
            },
        };
        var employeeAndStudent = new RoleFlags(false, true, false, true);

        // Employee alone would deny "students"; Student alone allows it -- most permissive wins.
        var decision = SharingPolicyEvaluator.Evaluate(employeeAndStudent, Group("students"), rolePolicy, NoOverride);

        Assert.True(decision.IsAllowed);
    }
}
