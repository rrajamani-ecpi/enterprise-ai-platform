namespace EnterpriseAIPlatform.Infrastructure.Authentication;

/// <summary>
/// Custom claim types written by <see cref="RoleClaimsTransformation"/> and read by the
/// current-user mapper. Role flags live here (post-transformation) so no consumer re-derives them.
/// </summary>
public static class AppClaimTypes
{
    public const string RolesTransformed = "eap:rolesTransformed";
    public const string IsAdmin = "eap:isAdmin";
    public const string IsEmployee = "eap:isEmployee";
    public const string IsContractor = "eap:isContractor";
    public const string IsStudent = "eap:isStudent";
    public const string AdvancedModelAccess = "eap:advancedModelAccess";
    public const string ImpersonateAsStudent = "eap:impersonateAsStudent";
}
