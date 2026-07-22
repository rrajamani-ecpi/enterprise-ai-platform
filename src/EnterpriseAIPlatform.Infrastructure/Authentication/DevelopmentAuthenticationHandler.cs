using System.Security.Claims;
using System.Text.Encodings.Web;
using EnterpriseAIPlatform.Application.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnterpriseAIPlatform.Infrastructure.Authentication;

/// <summary>Creates a configured local identity without contacting an external identity provider.</summary>
public sealed class DevelopmentAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Development";

    private readonly IHostEnvironment _environment;
    private readonly IOptionsMonitor<PlatformAuthenticationOptions> _platformOptions;

    public DevelopmentAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
        IOptionsMonitor<PlatformAuthenticationOptions> platformOptions,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IHostEnvironment environment)
        : base(schemeOptions, logger, encoder)
    {
        _platformOptions = platformOptions;
        _environment = environment;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var options = _platformOptions.CurrentValue;
        if (!_environment.IsDevelopment() || options.Mode != PlatformAuthenticationMode.Development)
        {
            return Task.FromResult(AuthenticateResult.Fail(
                "Development authentication may only run in the Development environment."));
        }

        var user = options.DevelopmentUser;
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Email),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim("name", user.Name),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("preferred_username", user.Email),
            new Claim(AppClaimTypes.RolesTransformed, "true"),
            BooleanClaim(AppClaimTypes.IsAdmin, user.IsAdmin),
            BooleanClaim(AppClaimTypes.IsEmployee, user.IsEmployee),
            BooleanClaim(AppClaimTypes.IsContractor, user.IsContractor),
            BooleanClaim(AppClaimTypes.IsStudent, user.IsStudent),
            BooleanClaim(AppClaimTypes.AdvancedModelAccess, user.AdvancedModelAccess),
            BooleanClaim(AppClaimTypes.ImpersonateAsStudent, false),
        };

        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static Claim BooleanClaim(string type, bool value) =>
        new(type, value ? "true" : "false", ClaimValueTypes.Boolean);
}
