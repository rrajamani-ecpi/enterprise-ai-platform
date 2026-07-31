using System.Collections.Concurrent;
using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Domain.Chat;

namespace EnterpriseAIPlatform.IntegrationTests;

/// <summary>
/// In-memory fake for <see cref="IMultiChatSessionStore"/> — no live Cosmos dependency in tests.
/// Delegates floor/cap enforcement to the same <see cref="MultiChatQuadrantRules"/> the real
/// <c>CosmosMultiChatSessionStore</c> uses, so fake and real behavior can't drift (spec 006 D2).
/// </summary>
public sealed class FakeMultiChatSessionStore : IMultiChatSessionStore
{
    private readonly ConcurrentDictionary<string, MultiChatSession> _sessions = new();

    public Task<MultiChatSession> GetOrCreateAsync(
        string ownerPartitionKey, string ownerUserId, CancellationToken cancellationToken = default)
    {
        var session = _sessions.GetOrAdd(ownerPartitionKey, key => new MultiChatSession
        {
            Id = $"multichat:{key}",
            PartitionKey = key,
            OwnerUserId = ownerUserId,
            Quadrants = new List<MultiChatQuadrant> { new() { Position = 0 }, new() { Position = 1 } },
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        return Task.FromResult(session);
    }

    public Task<ServerActionResponse<MultiChatSession>> AddQuadrantAsync(
        string ownerPartitionKey, CancellationToken cancellationToken = default)
    {
        var session = Require(ownerPartitionKey);
        return Task.FromResult(MultiChatQuadrantRules.TryAddQuadrant(session.Quadrants)
            ? ServerActionResponse<MultiChatSession>.Ok(session)
            : ServerActionResponse<MultiChatSession>.Error("A multi-chat session may not exceed 4 quadrants."));
    }

    public Task<MultiChatSession> RemoveQuadrantAsync(string ownerPartitionKey, CancellationToken cancellationToken = default)
    {
        var session = Require(ownerPartitionKey);
        MultiChatQuadrantRules.RemoveQuadrant(session.Quadrants);
        return Task.FromResult(session);
    }

    public Task<MultiChatSession> AssignModelAsync(
        string ownerPartitionKey, int position, string modelId, CancellationToken cancellationToken = default)
    {
        var session = Require(ownerPartitionKey);
        FindQuadrant(session, position).ModelId = modelId;
        return Task.FromResult(session);
    }

    public Task<MultiChatSession> SetQuadrantThreadAsync(
        string ownerPartitionKey, int position, string threadId, CancellationToken cancellationToken = default)
    {
        var session = Require(ownerPartitionKey);
        FindQuadrant(session, position).ThreadId = threadId;
        return Task.FromResult(session);
    }

    private MultiChatSession Require(string ownerPartitionKey) =>
        _sessions.TryGetValue(ownerPartitionKey, out var session)
            ? session
            : throw new InvalidOperationException("No multi-chat session exists yet for this caller; call GetOrCreateAsync first.");

    private static MultiChatQuadrant FindQuadrant(MultiChatSession session, int position) =>
        session.Quadrants.FirstOrDefault(q => q.Position == position)
            ?? throw new InvalidOperationException($"Quadrant {position} does not exist in this session.");
}
