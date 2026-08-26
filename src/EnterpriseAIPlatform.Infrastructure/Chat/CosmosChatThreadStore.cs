using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Domain.Chat;
using EnterpriseAIPlatform.Infrastructure.Persistence;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace EnterpriseAIPlatform.Infrastructure.Chat;

/// <summary>The single Cosmos-backed implementation of <see cref="IChatThreadStore"/> (spec 004 D1).</summary>
public sealed class CosmosChatThreadStore : IChatThreadStore
{
    private readonly CosmosClientProvider _clientProvider;
    private readonly CosmosOptions _options;

    public CosmosChatThreadStore(CosmosClientProvider clientProvider, IOptions<CosmosOptions> options)
    {
        _clientProvider = clientProvider;
        _options = options.Value;
    }

    private Container Container => _clientProvider.Client.GetContainer(_options.DatabaseName, _options.ChatContainerName);

    public async Task<ChatThreadModel?> GetAsync(
        string threadId, string ownerPartitionKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Container.ReadItemAsync<ChatThreadDocument>(
                threadId, new PartitionKey(ownerPartitionKey), cancellationToken: cancellationToken);
            return response.Resource.ToModel();
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<ChatThreadModel> CreateAsync(
        string ownerPartitionKey,
        string ownerUserId,
        string modelId,
        string? multiChatSessionId = null,
        int? multiChatPosition = null,
        CancellationToken cancellationToken = default)
    {
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

        await Container.CreateItemAsync(
            ChatThreadDocument.FromModel(thread), new PartitionKey(ownerPartitionKey), cancellationToken: cancellationToken);

        return thread;
    }

    public async Task<IReadOnlyList<ChatThreadModel>> ListByOwnerAsync(
        string ownerPartitionKey, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.PartitionKey = @pk AND c.Type = @type")
            .WithParameter("@pk", ownerPartitionKey)
            .WithParameter("@type", ChatThreadDocument.DocType);
        var options = new QueryRequestOptions { PartitionKey = new PartitionKey(ownerPartitionKey) };

        var threads = new List<ChatThreadModel>();
        using var iterator = Container.GetItemQueryIterator<ChatThreadDocument>(query, requestOptions: options);
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            threads.AddRange(page.Select(document => document.ToModel()));
        }

        return threads.OrderByDescending(thread => thread.LastActivityAtUtc).ToList();
    }

    public async Task<ServerActionResponse<ChatThreadModel>> RenameAsync(
        string threadId, string ownerPartitionKey, string newDisplayName, CancellationToken cancellationToken = default)
    {
        if (!ConversationRenameRules.TryValidate(newDisplayName, out var trimmed))
        {
            return ServerActionResponse<ChatThreadModel>.Error("A conversation name cannot be empty.");
        }

        var thread = await GetAsync(threadId, ownerPartitionKey, cancellationToken);
        if (thread is null)
        {
            return ServerActionResponse<ChatThreadModel>.NotFound("Conversation not found.");
        }

        thread.DisplayName = trimmed;
        await Container.UpsertItemAsync(
            ChatThreadDocument.FromModel(thread), new PartitionKey(ownerPartitionKey), cancellationToken: cancellationToken);

        return ServerActionResponse<ChatThreadModel>.Ok(thread);
    }

    public async Task TouchLastActivityAsync(
        string threadId, string ownerPartitionKey, CancellationToken cancellationToken = default)
    {
        var thread = await GetAsync(threadId, ownerPartitionKey, cancellationToken);
        if (thread is null)
        {
            return;
        }

        thread.LastActivityAtUtc = DateTimeOffset.UtcNow;
        await Container.UpsertItemAsync(
            ChatThreadDocument.FromModel(thread), new PartitionKey(ownerPartitionKey), cancellationToken: cancellationToken);
    }
}
