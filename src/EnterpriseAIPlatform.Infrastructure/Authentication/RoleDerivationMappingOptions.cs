using System.ComponentModel.DataAnnotations;
using EnterpriseAIPlatform.Application.Authorization;

namespace EnterpriseAIPlatform.Infrastructure.Authentication;

/// <summary>
/// The single configured Entra group-GUID -> role mapping (spec 002 FR-004, Constitution Principle V).
/// Validated at startup via ValidateDataAnnotations + ValidateOnStart.
/// </summary>
public sealed class RoleDerivationMappingOptions
{
    public const string SectionName = "RoleDerivation";

    [Required]
    [MinLength(1, ErrorMessage = "At least one role mapping must be configured.")]
    public List<RoleGroupMapping> Mappings { get; init; } = new();
}

public sealed class RoleGroupMapping
{
    public Guid GroupId { get; init; }

    public RoleName Role { get; init; }
}
