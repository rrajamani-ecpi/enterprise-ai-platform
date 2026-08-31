namespace EnterpriseAIPlatform.Domain.Personas;

/// <summary>
/// A read-only persona share target — an individual or a group token (spec 009 FR-008 Key
/// Entities). Constructed only via <see cref="ForIndividual"/>/<see cref="ForGroup"/> so
/// <see cref="Type"/> and exactly one of <see cref="IdentityPartitionKey"/>/<see cref="GroupToken"/>
/// can never disagree. This is 009's own type, independent of spec 018's <c>ShareTarget</c>
/// (research.md D8) — 009 does not consume spec 018's sharing policy yet.
/// </summary>
public sealed record PersonaShareTarget
{
    public required PersonaShareTargetType Type { get; init; }

    /// <summary>Set only when <see cref="Type"/> is <see cref="PersonaShareTargetType.Individual"/>. Hashed via <c>IIdentityHasher.ForEmail</c>.</summary>
    public string? IdentityPartitionKey { get; init; }

    /// <summary>Set only when <see cref="Type"/> is <see cref="PersonaShareTargetType.Group"/>. Opaque deployment-defined token (e.g. <c>@employees</c>).</summary>
    public string? GroupToken { get; init; }

    public static PersonaShareTarget ForIndividual(string identityPartitionKey) =>
        new() { Type = PersonaShareTargetType.Individual, IdentityPartitionKey = identityPartitionKey };

    public static PersonaShareTarget ForGroup(string groupToken) =>
        new() { Type = PersonaShareTargetType.Group, GroupToken = groupToken };
}
