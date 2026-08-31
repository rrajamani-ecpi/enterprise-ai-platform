using System.Security.Claims;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Domain.Identity;
using EnterpriseAIPlatform.Infrastructure.Authentication;

namespace EnterpriseAIPlatform.Infrastructure.Identity;

/// <summary>
/// Projects an already-transformed <see cref="ClaimsPrincipal"/> into a <see cref="UserModel"/>.
/// Reads the post-transformation role claims; it never re-derives roles (spec 002 FR-001).
/// </summary>
public static class PrincipalUserMapper
{
    public static UserModel FromPrincipal(ClaimsPrincipal principal)
    {
        var flags = new RoleFlags(
            ReadBool(principal, AppClaimTypes.IsAdmin),
            ReadBool(principal, AppClaimTypes.IsEmployee),
            ReadBool(principal, AppClaimTypes.IsContractor),
            ReadBool(principal, AppClaimTypes.IsStudent));

        return new UserModel
        {
            Name = principal.FindFirst("name")?.Value
                   ?? principal.FindFirst(ClaimTypes.Name)?.Value
                   ?? string.Empty,
            Email = principal.FindFirst("preferred_username")?.Value
                    ?? principal.FindFirst(ClaimTypes.Email)?.Value
                    ?? principal.FindFirst("email")?.Value
                    ?? string.Empty,
            Roles = flags,
            AdvancedModelAccess = ReadBool(principal, AppClaimTypes.AdvancedModelAccess),
            ImpersonateAsStudent = ReadBool(principal, AppClaimTypes.ImpersonateAsStudent),
            // Spec 016 FR-002: read from the transformed claim, never re-derived here — mirroring
            // how role flags are handled (spec 002 FR-001).
            GroupTokens = principal.FindAll(AppClaimTypes.GroupToken).Select(c => c.Value).ToArray(),
        };
    }

    private static bool ReadBool(ClaimsPrincipal principal, string claimType) =>
        bool.TryParse(principal.FindFirst(claimType)?.Value, out var value) && value;
}
