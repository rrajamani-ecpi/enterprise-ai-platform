using System.Security.Claims;
using System.Text.Encodings.Web;
using EnterpriseAIPlatform.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnterpriseAIPlatform.IntegrationTests;

/// <summary>
/// Test authentication handler: authenticates when the request carries an <c>X-Test-User</c> header,
/// and marks the principal admin when <c>X-Test-Admin: true</c> is present. Lets integration tests
/// exercise server-side route/admin authorization without a live Entra tenant.
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string AdminHeader = "X-Test-Admin";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user) || string.IsNullOrWhiteSpace(user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var isAdmin = Request.Headers.TryGetValue(AdminHeader, out var admin)
                      && string.Equals(admin, "true", StringComparison.OrdinalIgnoreCase);

        var claims = new List<Claim>
        {
            new("name", user.ToString()),
            new("preferred_username", user.ToString()),
            new(AppClaimTypes.RolesTransformed, "true"),
            new(AppClaimTypes.IsAdmin, isAdmin ? "true" : "false"),
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
