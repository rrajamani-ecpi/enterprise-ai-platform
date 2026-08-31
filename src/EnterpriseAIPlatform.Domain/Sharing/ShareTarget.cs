namespace EnterpriseAIPlatform.Domain.Sharing;

/// <summary>
/// A single share target — an individual or a group — with its access level (spec 018 FR-001 Key
/// Entities, FR-012/FR-013). Constructed only via <see cref="ForIndividual"/>/<see cref="ForGroup"/>
/// so <see cref="Type"/> and exactly one of <see cref="Identity"/>/<see cref="GroupToken"/> can never
/// disagree — the invariant is enforced structurally, not by a separate validation pass.
/// </summary>
public sealed record ShareTarget
{
    public required ShareTargetType Type { get; init; }

    /// <summary>Set only when <see cref="Type"/> is <see cref="ShareTargetType.Individual"/>.</summary>
    public string? Identity { get; init; }

    /// <summary>
    /// Set only when <see cref="Type"/> is <see cref="ShareTargetType.Group"/>. Corresponds to
    /// spec.md's <c>Group</c> Key Entity — represented here as an opaque string, not a distinct
    /// type, per spec Edge Cases (group catalogs are deployment-configured, not a fixed enum).
    /// </summary>
    public string? GroupToken { get; init; }

    /// <summary><c>Read</c> by default; <c>Collaborator</c> only when explicitly designated (FR-012/FR-013).</summary>
    public AccessLevel AccessLevel { get; init; } = AccessLevel.Read;

    public static ShareTarget ForIndividual(string identity, AccessLevel accessLevel = AccessLevel.Read) =>
        new() { Type = ShareTargetType.Individual, Identity = identity, AccessLevel = accessLevel };

    public static ShareTarget ForGroup(string groupToken, AccessLevel accessLevel = AccessLevel.Read) =>
        new() { Type = ShareTargetType.Group, GroupToken = groupToken, AccessLevel = accessLevel };
}
