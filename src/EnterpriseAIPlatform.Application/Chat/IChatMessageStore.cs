using EnterpriseAIPlatform.Domain.Chat;

namespace EnterpriseAIPlatform.Application.Chat;

/// <summary>The Cosmos-backed message store (spec 004 D1). Never called by <see cref="IChatPipeline"/> until every preflight gate passes (FR-001).</summary>
public interface IChatMessageStore
{
    Task AppendAsync(ChatMessageModel message, CancellationToken cancellationToken = default);

    /// <summary>Spec 024 US3 FR-006 — a thread's full message history, oldest first.</summary>
    Task<IReadOnlyList<ChatMessageModel>> ListByThreadAsync(
        string threadId, string ownerPartitionKey, CancellationToken cancellationToken = default);
}
