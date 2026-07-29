using EnterpriseAIPlatform.Domain.Identity;

namespace EnterpriseAIPlatform.Application.Authorization;

/// <summary>
/// The ONE place the impersonation role-downgrade is implemented (spec 002 FR-002/003, SC-002;
/// Constitution Principle IV). When impersonation is active, all elevated flags are forced off
/// and <c>IsStudent</c> is forced on. The operation is idempotent.
/// </summary>
public static class RoleDowngrade
{
    public static RoleFlags Apply(RoleFlags flags, bool impersonateAsStudent) =>
        impersonateAsStudent ? RoleFlags.StudentOnly : flags;
}
