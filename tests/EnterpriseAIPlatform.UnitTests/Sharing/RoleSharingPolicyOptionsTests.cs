using System.ComponentModel.DataAnnotations;
using EnterpriseAIPlatform.Application.Authorization;
using EnterpriseAIPlatform.Application.Sharing;

namespace EnterpriseAIPlatform.UnitTests.Sharing;

/// <summary>
/// Spec 018 data-model.md validation invariant: <c>Admin</c> MUST NOT be a configured key; other
/// non-admin roles are optional (fail-safe default) rather than required.
/// </summary>
public class RoleSharingPolicyOptionsTests
{
    private static List<ValidationResult> Validate(RoleSharingPolicyOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Rejects_AdminKey()
    {
        var options = new RoleSharingPolicyOptions
        {
            Roles = new Dictionary<RoleName, RolePolicy> { [RoleName.Admin] = RolePolicy.Default },
        };

        var results = Validate(options);

        Assert.NotEmpty(results);
    }

    [Fact]
    public void Accepts_MissingNonAdminRole_FallsBackToDefaultAtEvaluationTime()
    {
        // Contractor intentionally omitted -- spec 018 defines no policy for it (spec Assumptions addendum).
        var options = new RoleSharingPolicyOptions
        {
            Roles = new Dictionary<RoleName, RolePolicy>
            {
                [RoleName.Employee] = new() { GroupSharingEnabled = true, AllowedGroups = new[] { "faculty" } },
            },
        };

        var results = Validate(options);

        Assert.Empty(results);
        Assert.Equal(RolePolicy.Default, options.Roles.GetValueOrDefault(RoleName.Contractor, RolePolicy.Default));
    }
}
