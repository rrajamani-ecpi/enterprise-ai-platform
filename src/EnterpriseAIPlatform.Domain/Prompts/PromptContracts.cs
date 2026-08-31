namespace EnterpriseAIPlatform.Domain.Prompts;

/// <summary>Which write operation is being attempted, for <c>PromptAccessEvaluator</c> (spec 016 FR-001/FR-006).</summary>
public enum PromptOperation
{
    Edit,
    Delete,
    TransferOwnership,
}

/// <summary>
/// The client-facing projection of a <see cref="PromptModel"/>. Carries no server-only field; the
/// concurrency token is exposed so a caller can round-trip it, but never the raw entity.
/// </summary>
public sealed record PromptPublicDTO(
    string Id,
    string OwnerUserId,
    string Name,
    string Description,
    IReadOnlyList<string> CollaboratorPartitionKeys,
    IReadOnlyList<PromptShareTarget> SharedWith,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static PromptPublicDTO FromModel(PromptModel prompt) => new(
        prompt.Id,
        prompt.OwnerUserId,
        prompt.Name,
        prompt.Description,
        prompt.CollaboratorPartitionKeys,
        prompt.SharedWith,
        prompt.CreatedAtUtc,
        prompt.UpdatedAtUtc);
}

/// <summary>A prompt plus whether the calling user has favorited it (FR-016).</summary>
public sealed record PromptWithFavoriteDTO(PromptPublicDTO Prompt, bool IsFavorite);

/// <summary>
/// The ownership-transfer request body. Declares <b>exactly one</b> property by design: with no
/// <c>name</c>/<c>description</c>/<c>createdAt</c>/<c>sharedWith</c>/<c>ownerUserId</c> property for
/// the model binder to populate, forged values in a transfer payload are discarded before any
/// handler code runs. FR-005/SC-001 therefore hold structurally, not by a validation branch that
/// could be forgotten (contracts/authorization-policies.md).
/// </summary>
public sealed record TransferPromptOwnershipRequest(string NewOwnerEmail);
