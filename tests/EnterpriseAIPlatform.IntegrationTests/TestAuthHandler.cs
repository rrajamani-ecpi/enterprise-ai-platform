using System.Security.Claims;
using System.Text.Encodings.Web;
using EnterpriseAIPlatform.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnterpriseAIPlatform.IntegrationTests;

/// <summary>
/// Test authentication handler: authenticates when the request carries an <c>X-Test-User</c> header,
/// marks the principal admin when <c>X-Test-Admin: true</c> is present, and sets non-admin role
/// flags from a comma-separated <c>X-Test-Roles</c> header (e.g. <c>Employee,Student</c> — spec 009
/// research.md D9). Lets integration tests exercise server-side route/admin/role authorization
/// without a live Entra tenant.
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string AdminHeader = "X-Test-Admin";
    public const string RolesHeader = "X-Test-Roles";

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

        var roles = Request.Headers.TryGetValue(RolesHeader, out var rolesHeader)
            ? rolesHeader.ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();
        var isEmployee = roles.Contains("Employee", StringComparer.OrdinalIgnoreCase);
        var isContractor = roles.Contains("Contractor", StringComparer.OrdinalIgnoreCase);
        var isStudent = roles.Contains("Student", StringComparer.OrdinalIgnoreCase);

        var claims = new List<Claim>
        {
            new("name", user.ToString()),
            new("preferred_username", user.ToString()),
            new(AppClaimTypes.RolesTransformed, "true"),
            new(AppClaimTypes.IsAdmin, isAdmin ? "true" : "false"),
            new(AppClaimTypes.IsEmployee, isEmployee ? "true" : "false"),
            new(AppClaimTypes.IsContractor, isContractor ? "true" : "false"),
            new(AppClaimTypes.IsStudent, isStudent ? "true" : "false"),
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
