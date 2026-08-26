using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Domain.Chat;

namespace EnterpriseAIPlatform.Application.Chat;

/// <summary>The Cosmos-backed thread store (spec 004 D1). <see cref="CreateAsync"/> always creates a <c>"v3"</c> thread (FR-002).</summary>
public interface IChatThreadStore
{
    Task<ChatThreadModel?> GetAsync(string threadId, string ownerPartitionKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// <paramref name="multiChatSessionId"/>/<paramref name="multiChatPosition"/> are spec 006's
    /// extension (D5) — omitted (null) by every ordinary spec 004 call site.
    /// </summary>
    Task<ChatThreadModel> CreateAsync(
        string ownerPartitionKey,
        string ownerUserId,
        string modelId,
        string? multiChatSessionId = null,
        int? multiChatPosition = null,
        CancellationToken cancellationToken = default);

    /// <summary>Spec 024 US3 FR-005 — the caller's own conversations only, ordered most-recently-active first.</summary>
    Task<IReadOnlyList<ChatThreadModel>> ListByOwnerAsync(string ownerPartitionKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Spec 024 US3 FR-007. Returns <see cref="ResponseStatus.ERROR"/> for an empty/whitespace-only
    /// name (leaving the prior name in effect) and <see cref="ResponseStatus.NOT_FOUND"/> for a
    /// thread that doesn't exist or isn't owned by <paramref name="ownerPartitionKey"/> — the same
    /// outcome for both, so a foreign thread's existence is never revealed (FR-013).
    /// </summary>
    Task<ServerActionResponse<ChatThreadModel>> RenameAsync(
        string threadId, string ownerPartitionKey, string newDisplayName, CancellationToken cancellationToken = default);

    /// <summary>Spec 024 US3 FR-005 — bumps <see cref="ChatThreadModel.LastActivityAtUtc"/> to now; called by <c>IChatPipeline</c> after a message is persisted.</summary>
    Task TouchLastActivityAsync(string threadId, string ownerPartitionKey, CancellationToken cancellationToken = default);
}
