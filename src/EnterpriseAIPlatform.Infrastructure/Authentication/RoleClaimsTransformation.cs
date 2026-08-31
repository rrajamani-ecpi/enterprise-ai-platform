using System.Security.Claims;
using EnterpriseAIPlatform.Application.Authorization;
using Microsoft.AspNetCore.Authentication;

namespace EnterpriseAIPlatform.Infrastructure.Authentication;

/// <summary>
/// The single point where role flags are derived from Entra group claims AND the impersonation
/// downgrade is applied (spec 002 FR-002/003/004, SC-001/002). Runs once per authentication in the
/// ASP.NET Core pipeline, so every request/circuit observes identical flags.
/// </summary>
public sealed class RoleClaimsTransformation : IClaimsTransformation
{
    private readonly IRoleResolver _roleResolver;

    public RoleClaimsTransformation(IRoleResolver roleResolver) => _roleResolver = roleResolver;

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not { IsAuthenticated: true })
        {
            return Task.FromResult(principal);
        }

        // IClaimsTransformation can run multiple times per request; make it idempotent.
        if (principal.HasClaim(c => c.Type == AppClaimTypes.RolesTransformed))
        {
            return Task.FromResult(principal);
        }

        var groupClaimValues = principal.FindAll("groups")
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var groupIds = groupClaimValues
            .Select(v => Guid.TryParse(v, out var g) ? g : (Guid?)null)
            .Where(g => g is not null)
            .Select(g => g!.Value)
            .ToArray();

        var derived = _roleResolver.DeriveFrom(groupIds);
        var impersonate = ReadImpersonationFailClosed(principal);
        var flags = RoleDowngrade.Apply(derived, impersonate);

        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(AppClaimTypes.RolesTransformed, "true"));
        identity.AddClaim(new Claim(AppClaimTypes.IsAdmin, flags.IsAdmin ? "true" : "false"));
        identity.AddClaim(new Claim(AppClaimTypes.IsEmployee, flags.IsEmployee ? "true" : "false"));
        identity.AddClaim(new Claim(AppClaimTypes.IsContractor, flags.IsContractor ? "true" : "false"));
        identity.AddClaim(new Claim(AppClaimTypes.IsStudent, flags.IsStudent ? "true" : "false"));
        identity.AddClaim(new Claim(AppClaimTypes.ImpersonateAsStudent, impersonate ? "true" : "false"));

        // Spec 016 (research.md D6): retain the group values this transformation already reads,
        // instead of discarding them after deriving role flags. Group-token share targets
        // (PromptModel.SharedWith) are matched against these, so read access via a group grant is
        // resolvable server-side from a verified claim. Retained as raw strings, not parsed GUIDs —
        // a group token is opaque, and non-GUID values are legitimate for role derivation to ignore
        // while sharing still honours them.
        foreach (var groupValue in groupClaimValues)
        {
            identity.AddClaim(new Claim(AppClaimTypes.GroupToken, groupValue));
        }

        principal.AddIdentity(identity);
        return Task.FromResult(principal);
    }

    /// <summary>
    /// FR-009: if an impersonation signal exists but cannot be evaluated, fail closed
    /// (treat as impersonating -> most restrictive), never fail open to elevated access.
    /// </summary>
    private static bool ReadImpersonationFailClosed(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirst(AppClaimTypes.ImpersonateAsStudent)?.Value
                  ?? principal.FindFirst("impersonateAsStudent")?.Value;

        if (raw is null)
        {
            return false;
        }

        return bool.TryParse(raw, out var value) ? value : true;
    }
}
