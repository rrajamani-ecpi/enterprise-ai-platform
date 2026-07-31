using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Domain.Chat;
using EnterpriseAIPlatform.Infrastructure.Persistence;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace EnterpriseAIPlatform.Infrastructure.Chat;

/// <summary>
/// The single Cosmos-backed implementation of <see cref="IMultiChatSessionStore"/> (spec 006 D1/D2).
/// Every mutation leaves <c>Quadrants.Count</c> in [2, 4] — enforced here.
/// </summary>
public sealed class CosmosMultiChatSessionStore : IMultiChatSessionStore
{
    private readonly CosmosClientProvider _clientProvider;
    private readonly CosmosOptions _options;

    public CosmosMultiChatSessionStore(CosmosClientProvider clientProvider, IOptions<CosmosOptions> options)
    {
        _clientProvider = clientProvider;
        _options = options.Value;
    }

    private Container Container => _clientProvider.Client.GetContainer(_options.DatabaseName, _options.ChatContainerName);

    private static string SessionIdFor(string partitionKey) => $"multichat:{partitionKey}";

    public async Task<MultiChatSession> GetOrCreateAsync(
        string ownerPartitionKey, string ownerUserId, CancellationToken cancellationToken = default)
    {
        var existing = await TryGetAsync(ownerPartitionKey, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var session = new MultiChatSession
        {
            Id = SessionIdFor(ownerPartitionKey),
            PartitionKey = ownerPartitionKey,
            OwnerUserId = ownerUserId,
            Quadrants = new List<MultiChatQuadrant>
            {
                new() { Position = 0 },
                new() { Position = 1 },
            },
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        await Container.CreateItemAsync(
            MultiChatSessionDocument.FromModel(session), new PartitionKey(ownerPartitionKey), cancellationToken: cancellationToken);

        return session;
    }

    public async Task<ServerActionResponse<MultiChatSession>> AddQuadrantAsync(
        string ownerPartitionKey, CancellationToken cancellationToken = default)
    {
        var session = await RequireAsync(ownerPartitionKey, cancellationToken);
        if (!MultiChatQuadrantRules.TryAddQuadrant(session.Quadrants))
        {
            return ServerActionResponse<MultiChatSession>.Error("A multi-chat session may not exceed 4 quadrants.");
        }

        await SaveAsync(session, cancellationToken);
        return ServerActionResponse<MultiChatSession>.Ok(session);
    }

    public async Task<MultiChatSession> RemoveQuadrantAsync(string ownerPartitionKey, CancellationToken cancellationToken = default)
    {
        var session = await RequireAsync(ownerPartitionKey, cancellationToken);
        MultiChatQuadrantRules.RemoveQuadrant(session.Quadrants);
        await SaveAsync(session, cancellationToken);
        return session;
    }

    public async Task<MultiChatSession> AssignModelAsync(
        string ownerPartitionKey, int position, string modelId, CancellationToken cancellationToken = default)
    {
        var session = await RequireAsync(ownerPartitionKey, cancellationToken);
        var quadrant = FindQuadrant(session, position);
        quadrant.ModelId = modelId;
        await SaveAsync(session, cancellationToken);
        return session;
    }

    public async Task<MultiChatSession> SetQuadrantThreadAsync(
        string ownerPartitionKey, int position, string threadId, CancellationToken cancellationToken = default)
    {
        var session = await RequireAsync(ownerPartitionKey, cancellationToken);
        var quadrant = FindQuadrant(session, position);
        quadrant.ThreadId = threadId;
        await SaveAsync(session, cancellationToken);
        return session;
    }

    private static MultiChatQuadrant FindQuadrant(MultiChatSession session, int position) =>
        session.Quadrants.FirstOrDefault(q => q.Position == position)
            ?? throw new InvalidOperationException($"Quadrant {position} does not exist in this session.");

    private async Task<MultiChatSession?> TryGetAsync(string ownerPartitionKey, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Container.ReadItemAsync<MultiChatSessionDocument>(
                SessionIdFor(ownerPartitionKey), new PartitionKey(ownerPartitionKey), cancellationToken: cancellationToken);
            return response.Resource.ToModel();
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<MultiChatSession> RequireAsync(string ownerPartitionKey, CancellationToken cancellationToken) =>
        await TryGetAsync(ownerPartitionKey, cancellationToken)
        ?? throw new InvalidOperationException("No multi-chat session exists yet for this caller; call GetOrCreateAsync first.");

    private Task SaveAsync(MultiChatSession session, CancellationToken cancellationToken)
    {
        session.UpdatedAtUtc = DateTimeOffset.UtcNow;
        return Container.UpsertItemAsync(
            MultiChatSessionDocument.FromModel(session), new PartitionKey(session.PartitionKey), cancellationToken: cancellationToken);
    }
}
