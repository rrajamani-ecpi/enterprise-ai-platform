using EnterpriseAIPlatform.Application.Authorization;
using EnterpriseAIPlatform.Infrastructure.Authentication;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>Spec 002 US4 / FR-004 / SC-005: deterministic, independent role mapping.</summary>
public class RoleResolverTests
{
    private static readonly Guid AdminGroup = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid EmployeeGroup = Guid.Parse("00000000-0000-0000-0000-0000000000e2");
    private static readonly Guid StudentGroup = Guid.Parse("00000000-0000-0000-0000-0000000000c3");

    private static RoleResolver CreateResolver()
    {
        var options = Options.Create(new RoleDerivationMappingOptions
        {
            Mappings =
            {
                new RoleGroupMapping { GroupId = AdminGroup, Role = RoleName.Admin },
                new RoleGroupMapping { GroupId = EmployeeGroup, Role = RoleName.Employee },
                new RoleGroupMapping { GroupId = StudentGroup, Role = RoleName.Student },
            },
        });

        return new RoleResolver(options);
    }

    [Fact]
    public void DeriveFrom_AdminGroup_SetsAdminOnly()
    {
        var flags = CreateResolver().DeriveFrom(new[] { AdminGroup });

        Assert.True(flags.IsAdmin);
        Assert.False(flags.IsEmployee);
        Assert.False(flags.IsStudent);
    }

    [Fact]
    public void DeriveFrom_NoKnownGroups_SetsNothing()
    {
        var flags = CreateResolver().DeriveFrom(new[] { Guid.NewGuid() });

        Assert.False(flags.IsAdmin);
        Assert.False(flags.IsEmployee);
        Assert.False(flags.IsContractor);
        Assert.False(flags.IsStudent);
    }

    [Fact]
    public void DeriveFrom_MultipleGroups_SetsIndependentFlags()
    {
        var flags = CreateResolver().DeriveFrom(new[] { AdminGroup, EmployeeGroup });

        Assert.True(flags.IsAdmin);
        Assert.True(flags.IsEmployee);
        Assert.False(flags.IsStudent);
    }

    [Fact]
    public void Constructor_DuplicateGroupMapping_Throws()
    {
        var options = Options.Create(new RoleDerivationMappingOptions
        {
            Mappings =
            {
                new RoleGroupMapping { GroupId = AdminGroup, Role = RoleName.Admin },
                new RoleGroupMapping { GroupId = AdminGroup, Role = RoleName.Employee },
            },
        });

        Assert.Throws<InvalidOperationException>(() => new RoleResolver(options));
    }
}
