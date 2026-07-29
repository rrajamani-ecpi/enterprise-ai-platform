using System.Net;
using System.Net.Http.Json;

namespace EnterpriseAIPlatform.IntegrationTests;

/// <summary>
/// Spec 014 US1 / FR-001 / SC-001: every system-config, model-config, message-limit, and
/// persona-generation-model mutation is rejected for non-admins and succeeds for admins.
/// Non-admin rejection is enforced by the <c>RequireAdmin</c> authorization policy *before* the
/// endpoint delegate runs, so a 403 here structurally guarantees no write occurred.
/// </summary>
public sealed class ModelAccessAdminGateTests : IClassFixture<ModelAccessWebApplicationFactory>
{
    private readonly ModelAccessWebApplicationFactory _factory;

    public ModelAccessAdminGateTests(ModelAccessWebApplicationFactory factory) => _factory = factory;

    private HttpClient CreateClient(bool admin, string user)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user);
        if (admin)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.AdminHeader, "true");
        }

        return client;
    }

    [Fact]
    public async Task SystemConfig_NonAdmin_IsRejected()
    {
        var client = CreateClient(admin: false, user: "bob@contoso.com");

        var response = await client.PutAsJsonAsync("/api/admin/system-config", new
        {
            roleModelAccess = new Dictionary<string, string[]>(),
            fallbackModelId = "azure-foundry:gpt-5",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SystemConfig_Admin_Succeeds()
    {
        var client = CreateClient(admin: true, user: "admin@contoso.com");

        var response = await client.PutAsJsonAsync("/api/admin/system-config", new
        {
            roleModelAccess = new Dictionary<string, string[]> { ["default"] = new[] { "*" } },
            fallbackModelId = "azure-foundry:gpt-5",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ModelConfig_NonAdmin_IsRejected_AndCatalogUnchanged()
    {
        var client = CreateClient(admin: false, user: "bob@contoso.com");

        var response = await client.PutAsJsonAsync("/api/admin/model-config/test-provider:rejected-model", NewModel("test-provider:rejected-model"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var catalog = await client.GetFromJsonAsync<List<CatalogEntry>>("/api/model-catalog");
        Assert.DoesNotContain(catalog!, m => m.Id == "test-provider:rejected-model");
    }

    [Fact]
    public async Task ModelConfig_Admin_Succeeds()
    {
        var client = CreateClient(admin: true, user: "admin@contoso.com");

        var response = await client.PutAsJsonAsync("/api/admin/model-config/test-provider:accepted-model", NewModel("test-provider:accepted-model"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var catalog = await client.GetFromJsonAsync<List<CatalogEntry>>("/api/model-catalog");
        Assert.Contains(catalog!, m => m.Id == "test-provider:accepted-model");
    }

    [Fact]
    public async Task MessageLimit_NonAdmin_IsRejected_AndConfigUnchanged()
    {
        var client = CreateClient(admin: false, user: "bob@contoso.com");
        var before = await client.GetFromJsonAsync<MessageLimitDto>("/api/config/message-limit");

        var response = await client.PutAsJsonAsync("/api/admin/config/message-limit", new { perMessageCharacterCap = 999, dailyMessageCap = (int?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var after = await client.GetFromJsonAsync<MessageLimitDto>("/api/config/message-limit");
        Assert.Equal(before!.PerMessageCharacterCap, after!.PerMessageCharacterCap);
    }

    [Fact]
    public async Task MessageLimit_Admin_Succeeds()
    {
        var client = CreateClient(admin: true, user: "admin@contoso.com");

        var response = await client.PutAsJsonAsync("/api/admin/config/message-limit", new { perMessageCharacterCap = 5000, dailyMessageCap = 200 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PersonaGenerationModel_NonAdmin_IsRejected()
    {
        var client = CreateClient(admin: false, user: "bob@contoso.com");

        var response = await client.PutAsJsonAsync("/api/admin/config/persona-generation-model", new { allowedModelIds = new[] { "azure-foundry:gpt-5" } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PersonaGenerationModel_Admin_Succeeds()
    {
        var client = CreateClient(admin: true, user: "admin@contoso.com");

        var response = await client.PutAsJsonAsync("/api/admin/config/persona-generation-model", new { allowedModelIds = new[] { "azure-foundry:gpt-5" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static object NewModel(string id) => new
    {
        id,
        displayName = "Test Model",
        provider = "test-provider",
        isEnabled = true,
        requiresAdvancedModelAccess = false,
        supportsToolCalling = true,
        supportsVision = false,
        supportsReasoning = false,
        accessTier = "Standard",
        contextWindowSize = 8000,
        pricingInputPerMillionTokens = 0,
        pricingOutputPerMillionTokens = 0,
        isDeleted = false,
    };

    private sealed record CatalogEntry(string Id);

    private sealed record MessageLimitDto(int? PerMessageCharacterCap, int? DailyMessageCap);
}
