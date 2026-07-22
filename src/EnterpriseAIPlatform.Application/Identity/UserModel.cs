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

    public bool IsAdmin => Roles.IsAdmin;

    public bool IsEmployee => Roles.IsEmployee;

    public bool IsContractor => Roles.IsContractor;

    public bool IsStudent => Roles.IsStudent;
}
