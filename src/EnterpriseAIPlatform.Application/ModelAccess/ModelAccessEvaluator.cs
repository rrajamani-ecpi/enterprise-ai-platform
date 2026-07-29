using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Domain.ModelAccess;

namespace EnterpriseAIPlatform.Application.ModelAccess;

/// <summary>
/// The single implementation of the FR-003 intersection (Constitution Principle IV) — framework-free
/// so the full SC-002 combinatorial matrix (role x isEnabled x requiresAdvancedModelAccess x
/// advancedModelAccess) is unit-testable without a database.
/// </summary>
public static class ModelAccessEvaluator
{
    /// <summary>
    /// <c>AdvancedModelAccess</c> never overrides a missing role-allow-list entry — both
    /// conditions are required, never either/or (spec Edge Cases).
    /// </summary>
    public static bool ComputeEffectiveAccess(
        ModelConfigDocument model,
        UserModel caller,
        SystemModelConfig systemConfig)
    {
        if (!model.IsEnabled)
        {
            return false;
        }

        if (!IsRoleAllowListed(model.Id, caller, systemConfig.RoleModelAccess))
        {
            return false;
        }

        if (model.RequiresAdvancedModelAccess && !caller.AdvancedModelAccess)
        {
            return false;
        }

        return true;
    }

    private static bool IsRoleAllowListed(
        string modelId,
        UserModel caller,
        IReadOnlyDictionary<string, List<string>> roleModelAccess)
    {
        foreach (var roleName in RoleNamesFor(caller))
        {
            if (roleModelAccess.TryGetValue(roleName, out var allowList) && Allows(allowList, modelId))
            {
                return true;
            }
        }

        // "default" applies to every authenticated caller regardless of role (spec Key Entities).
        return roleModelAccess.TryGetValue("default", out var defaultList) && Allows(defaultList, modelId);
    }

    private static bool Allows(List<string> allowList, string modelId) =>
        allowList.Contains("*") || allowList.Contains(modelId);

    /// <summary>
    /// Spec 002's <see cref="UserModel"/> role flags (Admin/Employee/Contractor/Student) don't
    /// distinguish "staff" from "faculty" — both map to the "staff" bucket in this spec's
    /// role-model-access shape until a finer-grained flag exists upstream.
    /// </summary>
    private static IEnumerable<string> RoleNamesFor(UserModel caller)
    {
        if (caller.IsAdmin)
        {
            yield return "admin";
        }

        if (caller.IsEmployee)
        {
            yield return "staff";
        }

        if (caller.IsStudent)
        {
            yield return "student";
        }
    }
}
