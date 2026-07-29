using System.Net.Http.Json;

namespace EnterpriseAIPlatform.IntegrationTests;

/// <summary>Spec 014 US6 / FR-011 / SC-007 (single-provider, R1 scope): the seeded R1 model exposes complete catalog metadata.</summary>
public sealed class ModelCatalogMetadataTests : IClassFixture<ModelAccessWebApplicationFactory>
{
    private readonly ModelAccessWebApplicationFactory _factory;

    public ModelCatalogMetadataTests(ModelAccessWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task SeededR1Model_ExposesCompleteCatalogMetadata()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "erin@contoso.com");

        var catalog = await client.GetFromJsonAsync<List<CatalogEntry>>("/api/model-catalog");
        var entry = catalog!.Single(m => m.Id == "azure-foundry:gpt-5");

        Assert.Equal("azure-foundry", entry.Provider);
        Assert.False(string.IsNullOrWhiteSpace(entry.DisplayName));
        Assert.True(entry.SupportsToolCalling);
        Assert.True(entry.SupportsVision);
        Assert.True(entry.SupportsReasoning);
        Assert.Equal("Standard", entry.AccessTier);
    }

    private sealed record CatalogEntry(
        string Id, string DisplayName, string Provider,
        bool? SupportsToolCalling, bool? SupportsVision, bool? SupportsReasoning, string AccessTier);
}
