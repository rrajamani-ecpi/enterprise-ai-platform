using EnterpriseAIPlatform.Application.Common;

namespace EnterpriseAIPlatform.Application.Identity;

/// <summary>
/// The single canonical resolver for "who is the current user" (spec 002 FR-001).
/// Returns a structured <see cref="ServerActionResponse{T}"/> — never throws for a missing session (FR-005).
/// </summary>
public interface ICurrentUserAccessor
{
    ServerActionResponse<UserModel> GetCurrentUser();
}
