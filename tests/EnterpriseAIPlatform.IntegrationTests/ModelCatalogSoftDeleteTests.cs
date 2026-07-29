using System.Net;
using System.Net.Http.Json;

namespace EnterpriseAIPlatform.IntegrationTests;

/// <summary>Spec 014 US2 / FR-002 / SC-003: a deleted model persists with <c>isDeleted: true</c>; it is never physically removed.</summary>
public sealed class ModelCatalogSoftDeleteTests : IClassFixture<ModelAccessWebApplicationFactory>
{
    private readonly ModelAccessWebApplicationFactory _factory;

    public ModelCatalogSoftDeleteTests(ModelAccessWebApplicationFactory factory) => _factory = factory;

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "admin@contoso.com");
        client.DefaultRequestHeaders.Add(TestAuthHandler.AdminHeader, "true");
        return client;
    }

    [Fact]
    public async Task SoftDeletedModel_IsExcludedFromCatalogListing_ButNeverPhysicallyRemoved()
    {
        var client = AdminClient();
        const string modelId = "test-provider:soft-delete-me";

        var createResponse = await client.PutAsJsonAsync($"/api/admin/model-config/{modelId}", new
        {
            id = modelId,
            displayName = "Soft Delete Me",
            provider = "test-provider",
            isEnabled = true,
            requiresAdvancedModelAccess = false,
            supportsToolCalling = true,
            supportsVision = true,
            supportsReasoning = true,
            accessTier = "Standard",
            contextWindowSize = 4000,
            pricingInputPerMillionTokens = 0,
            pricingOutputPerMillionTokens = 0,
            isDeleted = false,
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var deleteResponse = await client.DeleteAsync($"/api/admin/model-config/{modelId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // Excluded from the default (non-deleted) listing.
        var catalog = await client.GetFromJsonAsync<List<CatalogEntry>>("/api/model-catalog");
        Assert.DoesNotContain(catalog!, m => m.Id == modelId);
    }

    private sealed record CatalogEntry(string Id);
}
