using System.Security.Claims;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Infrastructure.Authentication;
using EnterpriseAIPlatform.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>Spec 002 US2 / FR-005 / SC-003: no-session returns structured UNAUTHORIZED, never throws.</summary>
public class CurrentUserAccessorTests
{
    private static CurrentUserAccessor CreateAccessor(HttpContext? context)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(context);
        return new CurrentUserAccessor(accessor);
    }

    [Fact]
    public void GetCurrentUser_NoHttpContext_ReturnsUnauthorized()
    {
        var result = CreateAccessor(context: null).GetCurrentUser();

        Assert.Equal(ResponseStatus.UNAUTHORIZED, result.Status);
        Assert.Null(result.Response);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void GetCurrentUser_UnauthenticatedPrincipal_ReturnsUnauthorized()
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };

        var result = CreateAccessor(context).GetCurrentUser();

        Assert.Equal(ResponseStatus.UNAUTHORIZED, result.Status);
    }

    [Fact]
    public void GetCurrentUser_AuthenticatedPrincipal_ReturnsOkUser()
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim("name", "Alice"),
                new Claim("preferred_username", "alice@contoso.com"),
                new Claim(AppClaimTypes.IsAdmin, "true"),
            },
            authenticationType: "Test");
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        var result = CreateAccessor(context).GetCurrentUser();

        Assert.Equal(ResponseStatus.OK, result.Status);
        Assert.NotNull(result.Response);
        Assert.Equal("alice@contoso.com", result.Response!.Email);
        Assert.True(result.Response.IsAdmin);
    }
}
