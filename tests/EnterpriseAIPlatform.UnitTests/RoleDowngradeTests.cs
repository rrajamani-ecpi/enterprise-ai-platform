using EnterpriseAIPlatform.Application.Authorization;
using EnterpriseAIPlatform.Domain.Identity;
using Xunit;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>Spec 002 US1 / FR-002 / SC-002: the single downgrade implementation.</summary>
public class RoleDowngradeTests
{
    [Fact]
    public void Apply_WhenImpersonating_ForcesStudentOnly()
    {
        var elevated = new RoleFlags(IsAdmin: true, IsEmployee: true, IsContractor: true, IsStudent: false);

        var result = RoleDowngrade.Apply(elevated, impersonateAsStudent: true);

        Assert.Equal(RoleFlags.StudentOnly, result);
        Assert.False(result.IsAdmin);
        Assert.False(result.IsEmployee);
        Assert.False(result.IsContractor);
        Assert.True(result.IsStudent);
    }

    [Fact]
    public void Apply_WhenNotImpersonating_PassesFlagsThrough()
    {
        var elevated = new RoleFlags(IsAdmin: true, IsEmployee: false, IsContractor: false, IsStudent: false);

        var result = RoleDowngrade.Apply(elevated, impersonateAsStudent: false);

        Assert.Equal(elevated, result);
    }

    [Fact]
    public void Apply_IsIdempotent()
    {
        var elevated = new RoleFlags(IsAdmin: true, IsEmployee: true, IsContractor: false, IsStudent: false);

        var once = RoleDowngrade.Apply(elevated, impersonateAsStudent: true);
        var twice = RoleDowngrade.Apply(once, impersonateAsStudent: true);

        Assert.Equal(once, twice);
    }
}
