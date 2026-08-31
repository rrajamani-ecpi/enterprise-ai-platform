using EnterpriseAIPlatform.Domain.Identity;

namespace EnterpriseAIPlatform.Application.Identity;

/// <summary>
/// Session-derived projection of the current user (spec 002 Key Entities).
/// Intentionally carries NO token/secret field: secrets are excluded from this
/// client-facing model structurally, per Constitution Principle II / Security Constraints.
/// Role flags are read from the already-transformed principal — never re-derived here.
/// </summary>
public sealed record UserModel
{
    public required string Name { get; init; }

    public required string Email { get; init; }

    public string? Image { get; init; }

    public RoleFlags Roles { get; init; } = RoleFlags.None;

    public bool AdvancedModelAccess { get; init; }

    public bool ImpersonateAsStudent { get; init; }

    /// <summary>
    /// Opaque group tokens the caller belongs to, projected from the Entra <c>groups</c> claim by
    /// <c>RoleClaimsTransformation</c> (spec 016 research.md D6). Matched against a resource's
    /// group share targets — e.g. <c>PromptModel.SharedWith</c> (spec 016 FR-002).
    /// </summary>
    /// <remarks>
    /// Additive with an empty default so every pre-existing construction site (spec 002 onward)
    /// stays valid unchanged. Populated only from the server-verified <c>groups</c> claim — never
    /// from request data, query strings, or client state (Constitution Principle II).
    /// </remarks>
    public IReadOnlyList<string> GroupTokens { get; init; } = Array.Empty<string>();

    public bool IsAdmin => Roles.IsAdmin;

    public bool IsEmployee => Roles.IsEmployee;

    public bool IsContractor => Roles.IsContractor;

    public bool IsStudent => Roles.IsStudent;
}
