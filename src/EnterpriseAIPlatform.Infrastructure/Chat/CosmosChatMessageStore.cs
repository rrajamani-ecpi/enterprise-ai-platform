using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Domain.Chat;
using EnterpriseAIPlatform.Infrastructure.Persistence;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace EnterpriseAIPlatform.Infrastructure.Chat;

/// <summary>The single Cosmos-backed implementation of <see cref="IChatMessageStore"/> (spec 004 D1).</summary>
public sealed class CosmosChatMessageStore : IChatMessageStore
{
    private readonly CosmosClientProvider _clientProvider;
    private readonly CosmosOptions _options;

    public CosmosChatMessageStore(CosmosClientProvider clientProvider, IOptions<CosmosOptions> options)
    {
        _clientProvider = clientProvider;
        _options = options.Value;
    }

    private Container Container => _clientProvider.Client.GetContainer(_options.DatabaseName, _options.ChatContainerName);

    public Task AppendAsync(ChatMessageModel message, CancellationToken cancellationToken = default) =>
        Container.CreateItemAsync(
            ChatMessageDocument.FromModel(message), new PartitionKey(message.PartitionKey), cancellationToken: cancellationToken);
}
