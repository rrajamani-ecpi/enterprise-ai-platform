using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace EnterpriseAIPlatform.Infrastructure.Persistence;

/// <summary>
/// Lazily creates a single <see cref="CosmosClient"/> authenticated with workload identity
/// (<see cref="DefaultAzureCredential"/>) — no connection-string secret. The client is only
/// constructed on first access, so the app boots without a live Cosmos dependency in local dev.
/// </summary>
public sealed class CosmosClientProvider : IDisposable
{
    private readonly CosmosOptions _options;
    private readonly Lazy<CosmosClient> _client;

    public CosmosClientProvider(IOptions<CosmosOptions> options)
    {
        _options = options.Value;
        _client = new Lazy<CosmosClient>(CreateClient);
    }

    public CosmosClient Client => _client.Value;

    private CosmosClient CreateClient()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountEndpoint))
        {
            throw new InvalidOperationException(
                "Cosmos:AccountEndpoint is not configured; cannot create a CosmosClient.");
        }

        return new CosmosClient(_options.AccountEndpoint, new DefaultAzureCredential());
    }

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }
}
