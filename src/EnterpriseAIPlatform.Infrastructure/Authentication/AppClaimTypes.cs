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

    /// <summary>
    /// One claim per group the caller belongs to, projected from the Entra <c>groups</c> claim by
    /// <c>RoleClaimsTransformation</c> so <c>UserModel.GroupTokens</c> can carry it (spec 016
    /// research.md D6). Kept distinct from the raw <c>groups</c> claim so consumers read a value
    /// this app has explicitly transformed, matching how role flags are handled.
    /// </summary>
    public const string GroupToken = "eap:groupToken";
}
