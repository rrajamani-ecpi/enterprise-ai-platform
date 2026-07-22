using EnterpriseAIPlatform.Domain.Identity;

namespace EnterpriseAIPlatform.Application.Authorization;

/// <summary>
/// Single shared mapping from Entra group object-ids to role flags (spec 002 FR-004).
/// One implementation only (Constitution Principle IV).
/// </summary>
public interface IRoleResolver
{
    RoleFlags DeriveFrom(IReadOnlyCollection<Guid> entraGroupIds);
}
