using System.Collections.Concurrent;
using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.Chat;

namespace EnterpriseAIPlatform.IntegrationTests;

/// <summary>In-memory fake for <see cref="IChatThreadStore"/> — no live Cosmos dependency in tests.</summary>
public sealed class FakeChatThreadStore : IChatThreadStore
{
    private readonly ConcurrentDictionary<string, ChatThreadModel> _threads = new();

    /// <summary>Test-only seam (FR-024): simulates an unhandled infrastructure failure before any gate runs.</summary>
    public bool ThrowOnGet { get; set; }

    /// <summary>Test-only seam (spec 006 US2): on-demand creation fails for these multi-chat quadrant positions only.</summary>
    public HashSet<int> FailOnCreateForPositions { get; } = new();

    public Task<ChatThreadModel?> GetAsync(string threadId, string ownerPartitionKey, CancellationToken cancellationToken = default)
    {
        if (ThrowOnGet)
        {
            throw new InvalidOperationException("Simulated Cosmos read failure.");
        }

        return Task.FromResult(_threads.TryGetValue(threadId, out var thread) && thread.PartitionKey == ownerPartitionKey ? thread : null);
    }

    public Task<ChatThreadModel> CreateAsync(
        string ownerPartitionKey,
        string ownerUserId,
        string modelId,
        string? multiChatSessionId = null,
        int? multiChatPosition = null,
        CancellationToken cancellationToken = default)
    {
        if (multiChatPosition is int position && FailOnCreateForPositions.Contains(position))
        {
            throw new InvalidOperationException($"Simulated thread-creation failure for quadrant {position}.");
        }

        var createdAtUtc = DateTimeOffset.UtcNow;
        var thread = new ChatThreadModel
        {
            Id = Guid.NewGuid().ToString("n"),
            PartitionKey = ownerPartitionKey,
            OwnerUserId = ownerUserId,
            Version = "v3",
            ModelId = modelId,
            CreatedAtUtc = createdAtUtc,
            DisplayName = $"Conversation — {createdAtUtc:MMM d, yyyy h:mm tt}",
            LastActivityAtUtc = createdAtUtc,
            MultiChatSessionId = multiChatSessionId,
            MultiChatPosition = multiChatPosition,
        };
        _threads[thread.Id] = thread;
        return Task.FromResult(thread);
    }

    public Task<IReadOnlyList<ChatThreadModel>> ListByOwnerAsync(
        string ownerPartitionKey, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ChatThreadModel> result = _threads.Values
            .Where(thread => thread.PartitionKey == ownerPartitionKey)
            .OrderByDescending(thread => thread.LastActivityAtUtc)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<ServerActionResponse<ChatThreadModel>> RenameAsync(
        string threadId, string ownerPartitionKey, string newDisplayName, CancellationToken cancellationToken = default)
    {
        if (!ConversationRenameRules.TryValidate(newDisplayName, out var trimmed))
        {
            return Task.FromResult(ServerActionResponse<ChatThreadModel>.Error("A conversation name cannot be empty."));
        }

        if (!_threads.TryGetValue(threadId, out var thread) || thread.PartitionKey != ownerPartitionKey)
        {
            return Task.FromResult(ServerActionResponse<ChatThreadModel>.NotFound("Conversation not found."));
        }

        thread.DisplayName = trimmed;
        return Task.FromResult(ServerActionResponse<ChatThreadModel>.Ok(thread));
    }

    public Task TouchLastActivityAsync(string threadId, string ownerPartitionKey, CancellationToken cancellationToken = default)
    {
        if (_threads.TryGetValue(threadId, out var thread) && thread.PartitionKey == ownerPartitionKey)
        {
            thread.LastActivityAtUtc = DateTimeOffset.UtcNow;
        }

        return Task.CompletedTask;
    }

    /// <summary>Test-only seam so tests can plant a non-"v3" thread (US1's read-only-thread scenario).</summary>
    public void Seed(ChatThreadModel thread) => _threads[thread.Id] = thread;
}

/// <summary>In-memory fake for <see cref="IChatMessageStore"/> — no live Cosmos dependency in tests.</summary>
public sealed class FakeChatMessageStore : IChatMessageStore
{
    private readonly ConcurrentBag<ChatMessageModel> _messages = new();

    public IReadOnlyList<ChatMessageModel> Messages => _messages.ToList();

    public Task AppendAsync(ChatMessageModel message, CancellationToken cancellationToken = default)
    {
        _messages.Add(message);
        return Task.CompletedTask;
    }

    /// <summary>Test-only seam so tests can plant message history directly (spec 024 US3).</summary>
    public void Seed(ChatMessageModel message) => _messages.Add(message);

    public Task<IReadOnlyList<ChatMessageModel>> ListByThreadAsync(
        string threadId, string ownerPartitionKey, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ChatMessageModel> result = _messages
            .Where(message => message.ThreadId == threadId && message.PartitionKey == ownerPartitionKey)
            .OrderBy(message => message.CreatedAtUtc)
            .ToList();
        return Task.FromResult(result);
    }
}

/// <summary>Deterministic fake for <see cref="IChatCompletionClient"/> — no live Azure/Foundry dependency in tests.</summary>
public sealed class FakeChatCompletionClient : IChatCompletionClient
{
    public string Provider => "azure-foundry";

    public string[] Chunks { get; set; } = { "Hello", ", ", "world", "!" };

    public bool ThrowOnStream { get; set; }

    public async IAsyncEnumerable<string> StreamCompletionAsync(
        ChatRequest request,
        Domain.ModelAccess.ModelConfigDocument model,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (ThrowOnStream)
        {
            throw new InvalidOperationException("Simulated model-invocation failure.");
        }

        foreach (var chunk in Chunks)
        {
            await Task.Yield();
            yield return chunk;
        }
    }
}
