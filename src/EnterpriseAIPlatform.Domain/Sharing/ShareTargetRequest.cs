namespace EnterpriseAIPlatform.Domain.Sharing;

/// <summary>
/// Evaluator input: what kind of target a caller is attempting to share with, and which group if
/// applicable. Deliberately narrower than <see cref="ShareTarget"/> — the evaluator has no opinion
/// on the target's identity value or requested access level (spec 018 data-model.md).
/// </summary>
/// <param name="Type">What kind of target is being requested.</param>
/// <param name="GroupToken">Set only when <see cref="Type"/> is <see cref="ShareTargetType.Group"/>; ignored otherwise (FR-004/FR-005 — individual sharing is not group-gated).</param>
public sealed record ShareTargetRequest(ShareTargetType Type, string? GroupToken = null);
