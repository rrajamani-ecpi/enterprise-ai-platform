using EnterpriseAIPlatform.Domain.Chat;

namespace EnterpriseAIPlatform.Application.Chat;

/// <summary>The Cosmos-backed thread store (spec 004 D1). <see cref="CreateAsync"/> always creates a <c>"v3"</c> thread (FR-002).</summary>
public interface IChatThreadStore
{
    Task<ChatThreadModel?> GetAsync(string threadId, string ownerPartitionKey, CancellationToken cancellationToken = default);

    Task<ChatThreadModel> CreateAsync(
        string ownerPartitionKey, string ownerUserId, string modelId, CancellationToken cancellationToken = default);
}
