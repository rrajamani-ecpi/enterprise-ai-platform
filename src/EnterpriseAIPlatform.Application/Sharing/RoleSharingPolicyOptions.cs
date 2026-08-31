using System.ComponentModel.DataAnnotations;
using EnterpriseAIPlatform.Application.Authorization;

namespace EnterpriseAIPlatform.Application.Sharing;

/// <summary>
/// Per-role group-sharing eligibility (spec 018 FR-002/FR-004/FR-005). Individual sharing is
/// always allowed for non-admin roles and is not represented here — that rule, and admin's
/// allow-everything behavior, are hardcoded in <see cref="SharingPolicyEvaluator"/>, never
/// config-driven (research.md D3). Bound from the "RoleSharing" config section; validated at
/// startup via <c>ValidateDataAnnotations().ValidateOnStart()</c>.
/// </summary>
/// <remarks>
/// Lives in Application, not Infrastructure, because it is keyed by <see cref="RoleName"/> (also
/// Application) and consumed directly by <see cref="SharingPolicyEvaluator"/> (a pure Application-
/// layer function) — placing it in Infrastructure would create a reverse Application→Infrastructure
/// dependency. Infrastructure still owns the actual <c>IOptions</c> binding/DI registration.
/// </remarks>
public sealed class RoleSharingPolicyOptions : IValidatableObject
{
    public const string SectionName = "RoleSharing";

    /// <summary>
    /// Any non-admin <see cref="RoleName"/> may be omitted — an omitted role (e.g. <c>Contractor</c>,
    /// which spec.md defines no policy for) falls back to <see cref="RolePolicy.Default"/> rather
    /// than failing startup or guessing a permissive value (spec Assumptions addendum).
    /// </summary>
    public Dictionary<RoleName, RolePolicy> Roles { get; init; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Roles.ContainsKey(RoleName.Admin))
        {
            yield return new ValidationResult(
                $"{SectionName}:Roles MUST NOT contain an '{RoleName.Admin}' entry — admin's " +
                "allow-everything behavior is hardcoded, never config-driven (spec 018 FR-003).",
                new[] { nameof(Roles) });
        }
    }
}

/// <summary>
/// A role's group-sharing entitlement. A <see cref="RoleName"/> absent from
/// <see cref="RoleSharingPolicyOptions.Roles"/> defaults to <see cref="Default"/> — the most
/// restrictive policy (spec 018 data-model.md fail-safe default).
/// </summary>
public sealed record RolePolicy
{
    public static readonly RolePolicy Default = new()
    {
        GroupSharingEnabled = false,
        AllowedGroups = Array.Empty<string>(),
    };

    public bool GroupSharingEnabled { get; init; }

    public string[] AllowedGroups { get; init; } = Array.Empty<string>();
}
