using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.Identity;
using EnterpriseAIPlatform.Domain.ModelAccess;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>
/// Spec 014 US2 / FR-003 / SC-002: the role x isEnabled x requiresAdvancedModelAccess x
/// advancedModelAccess intersection matrix.
/// </summary>
public class ModelAccessEvaluatorTests
{
    private static ModelConfigDocument Model(bool isEnabled = true, bool requiresAdvanced = false) => new()
    {
        Id = "azure-foundry:gpt-5",
        DisplayName = "GPT-5",
        Provider = "azure-foundry",
        IsEnabled = isEnabled,
        RequiresAdvancedModelAccess = requiresAdvanced,
        SupportsToolCalling = true,
        SupportsVision = true,
        SupportsReasoning = true,
        AccessTier = ModelAccessTier.Standard,
    };

    private static UserModel Caller(bool isStudent = true, bool advancedModelAccess = false) => new()
    {
        Name = "Student One",
        Email = "student@contoso.com",
        Roles = new RoleFlags(IsAdmin: false, IsEmployee: false, IsContractor: false, IsStudent: isStudent),
        AdvancedModelAccess = advancedModelAccess,
    };

    private static SystemModelConfig SystemConfig(Dictionary<string, List<string>>? roleModelAccess = null) => new()
    {
        RoleModelAccess = roleModelAccess ?? new Dictionary<string, List<string>>
        {
            ["student"] = new List<string> { "azure-foundry:gpt-5" },
        },
        FallbackModelId = "azure-foundry:gpt-5",
    };

    [Fact]
    public void Enabled_RoleAllowListed_NoAdvancedRequired_IsIncluded()
    {
        var result = ModelAccessEvaluator.ComputeEffectiveAccess(Model(), Caller(), SystemConfig());

        Assert.True(result);
    }

    [Fact]
    public void RequiresAdvanced_CallerLacksIt_IsExcluded_EvenIfEnabledAndRoleAllowListed()
    {
        var result = ModelAccessEvaluator.ComputeEffectiveAccess(
            Model(requiresAdvanced: true), Caller(advancedModelAccess: false), SystemConfig());

        Assert.False(result);
    }

    [Fact]
    public void RequiresAdvanced_CallerHasIt_AndRoleAllowListed_IsIncluded()
    {
        var result = ModelAccessEvaluator.ComputeEffectiveAccess(
            Model(requiresAdvanced: true), Caller(advancedModelAccess: true), SystemConfig());

        Assert.True(result);
    }

    [Fact]
    public void Disabled_IsExcluded_RegardlessOfAdvancedAccessFlag()
    {
        var result = ModelAccessEvaluator.ComputeEffectiveAccess(
            Model(isEnabled: false), Caller(advancedModelAccess: true), SystemConfig());

        Assert.False(result);
    }

    [Fact]
    public void NotInCallersRoleAllowList_IsExcluded_RegardlessOfAdvancedAccessFlag()
    {
        var systemConfig = SystemConfig(new Dictionary<string, List<string>> { ["admin"] = new List<string> { "azure-foundry:gpt-5" } });

        var result = ModelAccessEvaluator.ComputeEffectiveAccess(
            Model(), Caller(isStudent: true, advancedModelAccess: true), systemConfig);

        Assert.False(result);
    }

    [Fact]
    public void AdvancedAccess_NeverOverridesMissingRoleAllowListEntry()
    {
        // Edge Case: AdvancedModelAccess=true must never substitute for a missing role entry.
        var systemConfig = SystemConfig(new Dictionary<string, List<string>>());

        var result = ModelAccessEvaluator.ComputeEffectiveAccess(
            Model(requiresAdvanced: true), Caller(advancedModelAccess: true), systemConfig);

        Assert.False(result);
    }

    [Fact]
    public void WildcardRoleAllowList_Includes_AnyModel()
    {
        var systemConfig = SystemConfig(new Dictionary<string, List<string>> { ["student"] = new List<string> { "*" } });

        var result = ModelAccessEvaluator.ComputeEffectiveAccess(Model(), Caller(), systemConfig);

        Assert.True(result);
    }

    [Fact]
    public void DefaultBucket_AppliesRegardlessOfRole()
    {
        var caller = new UserModel { Name = "No Role", Email = "norole@contoso.com" };
        var systemConfig = SystemConfig(new Dictionary<string, List<string>> { ["default"] = new List<string> { "*" } });

        var result = ModelAccessEvaluator.ComputeEffectiveAccess(Model(), caller, systemConfig);

        Assert.True(result);
    }
}
