using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Domain.Chat;

namespace EnterpriseAIPlatform.Application.Chat;

/// <summary>
/// The single implementation guarantees <c>Quadrants.Count</c> is always in [2, 4] after any call
/// (spec 006 FR-004/005) — enforced here, not by the caller.
/// </summary>
public interface IMultiChatSessionStore
{
    /// <summary>A caller with no session yet gets a fresh 2-quadrant default (FR-002).</summary>
    Task<MultiChatSession> GetOrCreateAsync(
        string ownerPartitionKey, string ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Rejects at the 4-quadrant cap (FR-005).</summary>
    Task<ServerActionResponse<MultiChatSession>> AddQuadrantAsync(
        string ownerPartitionKey, CancellationToken cancellationToken = default);

    /// <summary>At the 2-quadrant floor, clears the highest-position quadrant's assignment instead of removing it (FR-004).</summary>
    Task<MultiChatSession> RemoveQuadrantAsync(string ownerPartitionKey, CancellationToken cancellationToken = default);

    Task<MultiChatSession> AssignModelAsync(
        string ownerPartitionKey, int position, string modelId, CancellationToken cancellationToken = default);

    /// <summary>Sets <c>ThreadId</c> on a specific quadrant — called once per quadrant's first send (FR-003).</summary>
    Task<MultiChatSession> SetQuadrantThreadAsync(
        string ownerPartitionKey, int position, string threadId, CancellationToken cancellationToken = default);
}
