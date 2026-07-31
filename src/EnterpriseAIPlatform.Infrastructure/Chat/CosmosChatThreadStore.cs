using EnterpriseAIPlatform.Application.Chat;
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
        var thread = new ChatThreadModel
        {
            Id = Guid.NewGuid().ToString("n"),
            PartitionKey = ownerPartitionKey,
            OwnerUserId = ownerUserId,
            Version = "v3",
            ModelId = modelId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            MultiChatSessionId = multiChatSessionId,
            MultiChatPosition = multiChatPosition,
        };

        await Container.CreateItemAsync(
            ChatThreadDocument.FromModel(thread), new PartitionKey(ownerPartitionKey), cancellationToken: cancellationToken);

        return thread;
    }
}
