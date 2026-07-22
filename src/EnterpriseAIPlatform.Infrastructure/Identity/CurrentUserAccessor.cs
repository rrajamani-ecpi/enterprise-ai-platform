using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using Microsoft.AspNetCore.Http;

namespace EnterpriseAIPlatform.Infrastructure.Identity;

/// <summary>
/// The single canonical current-user resolver (spec 002 FR-001). Returns a structured
/// UNAUTHORIZED response (never throws) when there is no active authenticated session (FR-005).
/// </summary>
public sealed class CurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserAccessor(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor;

    public ServerActionResponse<UserModel> GetCurrentUser()
    {
        var principal = _httpContextAccessor.HttpContext?.User;

        if (principal?.Identity is not { IsAuthenticated: true })
        {
            return ServerActionResponse<UserModel>.Unauthorized();
        }

        return ServerActionResponse<UserModel>.Ok(PrincipalUserMapper.FromPrincipal(principal));
    }
}
