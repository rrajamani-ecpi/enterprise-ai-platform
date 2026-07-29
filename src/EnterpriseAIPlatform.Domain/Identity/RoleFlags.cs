namespace EnterpriseAIPlatform.Domain.Identity;

/// <summary>
/// Immutable set of role flags derived from Entra group membership.
/// Independent (not mutually exclusive) per spec 002 User Story 4.
/// </summary>
public readonly record struct RoleFlags(bool IsAdmin, bool IsEmployee, bool IsContractor, bool IsStudent)
{
    /// <summary>Most-restrictive flag set (deny all elevation).</summary>
    public static RoleFlags None => new(false, false, false, false);

    /// <summary>The downgraded result of impersonation: student only.</summary>
    public static RoleFlags StudentOnly => new(false, false, false, true);
}
