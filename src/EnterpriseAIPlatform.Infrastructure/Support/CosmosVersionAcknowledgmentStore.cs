using EnterpriseAIPlatform.Application.Support;
using EnterpriseAIPlatform.Domain.Support;
using EnterpriseAIPlatform.Infrastructure.Persistence;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace EnterpriseAIPlatform.Infrastructure.Support;

/// <summary>
/// The single Cosmos-backed implementation of <see cref="IVersionAcknowledgmentStore"/> (spec 017
/// FR-012). Uses spec 002's <c>users</c> container — declared since spec 002 but not used by any
/// concrete store until now.
/// </summary>
public sealed class CosmosVersionAcknowledgmentStore : IVersionAcknowledgmentStore
{
    private readonly CosmosClientProvider _clientProvider;
    private readonly CosmosOptions _options;

    public CosmosVersionAcknowledgmentStore(CosmosClientProvider clientProvider, IOptions<CosmosOptions> options)
    {
        _clientProvider = clientProvider;
        _options = options.Value;
    }

    private Container Container => _clientProvider.Client.GetContainer(_options.DatabaseName, _options.UserContainerName);

    public async Task<VersionAcknowledgmentModel?> GetAsync(
        string ownerPartitionKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Container.ReadItemAsync<VersionAcknowledgmentDocument>(
                DocumentId(ownerPartitionKey), new PartitionKey(ownerPartitionKey), cancellationToken: cancellationToken);
            return response.Resource.ToModel();
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task SetAsync(
        string ownerPartitionKey, string acknowledgedVersion, CancellationToken cancellationToken = default)
    {
        var document = new VersionAcknowledgmentDocument
        {
            Id = DocumentId(ownerPartitionKey),
            PartitionKey = ownerPartitionKey,
            AcknowledgedVersion = acknowledgedVersion,
            AcknowledgedAtUtc = DateTimeOffset.UtcNow,
        };

        await Container.UpsertItemAsync(document, new PartitionKey(ownerPartitionKey), cancellationToken: cancellationToken);
    }

    private static string DocumentId(string ownerPartitionKey) => $"version-ack:{ownerPartitionKey}";

    private sealed class VersionAcknowledgmentDocument
    {
        [JsonProperty("id")]
        public required string Id { get; set; }

        public required string PartitionKey { get; set; }

        public required string AcknowledgedVersion { get; set; }

        public DateTimeOffset AcknowledgedAtUtc { get; set; }

        public VersionAcknowledgmentModel ToModel() => new()
        {
            Id = Id,
            PartitionKey = PartitionKey,
            AcknowledgedVersion = AcknowledgedVersion,
            AcknowledgedAtUtc = AcknowledgedAtUtc,
        };
    }
}
