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
}
