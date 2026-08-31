using EnterpriseAIPlatform.Domain.Sharing;

namespace EnterpriseAIPlatform.Domain.Prompts;

/// <summary>
/// A read-only prompt share target — an individual or a group token (spec 016 FR-002). Constructed
/// only via <see cref="ForIndividual"/>/<see cref="ForGroup"/> so <see cref="Type"/> and exactly one
/// of <see cref="IdentityPartitionKey"/>/<see cref="GroupToken"/> can never disagree.
/// </summary>
/// <remarks>
/// Reuses spec 018's <see cref="ShareTargetType"/> rather than declaring a prompt-local enum
/// (Constitution Principle IV) — unlike spec 009's <c>PersonaShareTarget</c>, which predates 016's
/// adoption of the canonical sharing policy (research.md D5).
/// </remarks>
public sealed record PromptShareTarget
{
    public required ShareTargetType Type { get; init; }

    /// <summary>Set only when <see cref="Type"/> is <see cref="ShareTargetType.Individual"/>. Hashed via <c>IIdentityHasher.ForEmail</c> — never a raw email.</summary>
    public string? IdentityPartitionKey { get; init; }

    /// <summary>Set only when <see cref="Type"/> is <see cref="ShareTargetType.Group"/>. Matched against <c>UserModel.GroupTokens</c> (research.md D6).</summary>
    public string? GroupToken { get; init; }

    public static PromptShareTarget ForIndividual(string identityPartitionKey) =>
        new() { Type = ShareTargetType.Individual, IdentityPartitionKey = identityPartitionKey };

    public static PromptShareTarget ForGroup(string groupToken) =>
        new() { Type = ShareTargetType.Group, GroupToken = groupToken };
}
