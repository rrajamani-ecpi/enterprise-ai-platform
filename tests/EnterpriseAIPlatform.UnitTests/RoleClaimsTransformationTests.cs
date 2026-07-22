using System.Security.Claims;
using EnterpriseAIPlatform.Application.Authorization;
using EnterpriseAIPlatform.Infrastructure.Authentication;
using EnterpriseAIPlatform.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>
/// Spec 002 US1 / SC-001: role derivation + impersonation downgrade happen once, in the
/// claims transformation, and every read path (the mapper/accessor) agrees without re-deriving.
/// </summary>
public class RoleClaimsTransformationTests
{
    private static readonly Guid AdminGroup = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private static RoleClaimsTransformation CreateTransformation()
    {
        var options = Options.Create(new RoleDerivationMappingOptions
        {
            Mappings = { new RoleGroupMapping { GroupId = AdminGroup, Role = RoleName.Admin } },
        });

        return new RoleClaimsTransformation(new RoleResolver(options));
    }

    private static ClaimsPrincipal AuthenticatedPrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Test"));

    [Fact]
    public async Task Transform_AdminWithImpersonation_DowngradesConsistently()
    {
        var principal = AuthenticatedPrincipal(
            new Claim("groups", AdminGroup.ToString()),
            new Claim(AppClaimTypes.ImpersonateAsStudent, "true"));

        var transformed = await CreateTransformation().TransformAsync(principal);
        var mapped = PrincipalUserMapper.FromPrincipal(transformed);

        Assert.False(mapped.IsAdmin);
        Assert.True(mapped.IsStudent);
        Assert.True(mapped.ImpersonateAsStudent);
    }

    [Fact]
    public async Task Transform_AdminWithoutImpersonation_KeepsAdmin()
    {
        var principal = AuthenticatedPrincipal(new Claim("groups", AdminGroup.ToString()));

        var transformed = await CreateTransformation().TransformAsync(principal);
        var mapped = PrincipalUserMapper.FromPrincipal(transformed);

        Assert.True(mapped.IsAdmin);
        Assert.False(mapped.IsStudent);
    }

    [Fact]
    public async Task Transform_IsIdempotent_WhenRunTwice()
    {
        var transformation = CreateTransformation();
        var principal = AuthenticatedPrincipal(new Claim("groups", AdminGroup.ToString()));

        var once = await transformation.TransformAsync(principal);
        var twice = await transformation.TransformAsync(once);

        Assert.Single(twice.FindAll(AppClaimTypes.IsAdmin));
    }

    [Fact]
    public async Task Transform_MalformedImpersonation_FailsClosed()
    {
        var principal = AuthenticatedPrincipal(
            new Claim("groups", AdminGroup.ToString()),
            new Claim(AppClaimTypes.ImpersonateAsStudent, "not-a-bool"));

        var transformed = await CreateTransformation().TransformAsync(principal);
        var mapped = PrincipalUserMapper.FromPrincipal(transformed);

        Assert.False(mapped.IsAdmin);
        Assert.True(mapped.IsStudent);
    }
}
