namespace EnterpriseAIPlatform.Infrastructure.Persistence;

/// <summary>Configuration for the user-scoped Cosmos DB store (partition-keyed by hashed identity).</summary>
public sealed class CosmosOptions
{
    public const string SectionName = "Cosmos";

    public string? AccountEndpoint { get; init; }

    public string DatabaseName { get; init; } = "eap";

    public string UserContainerName { get; init; } = "users";
}
