using System.Net;
using System.Net.Http.Json;

namespace EnterpriseAIPlatform.IntegrationTests;

/// <summary>Spec 014 US3 / FR-004-007 / SC-004: message-limit and persona-generation-model configs stay readable while remaining write-protected.</summary>
public sealed class ConfigReadWriteAsymmetryTests : IClassFixture<ModelAccessWebApplicationFactory>
{
    private readonly ModelAccessWebApplicationFactory _factory;

    public ConfigReadWriteAsymmetryTests(ModelAccessWebApplicationFactory factory) => _factory = factory;

    private HttpClient NonAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "carol@contoso.com");
        return client;
    }

    [Fact]
    public async Task NonAdmin_CanRead_MessageLimitConfig()
    {
        var response = await NonAdminClient().GetAsync("/api/config/message-limit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NonAdmin_CanRead_PersonaGenerationModelConfig()
    {
        var response = await NonAdminClient().GetAsync("/api/config/persona-generation-model");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NonAdmin_CannotWrite_MessageLimitConfig()
    {
        var response = await NonAdminClient().PutAsJsonAsync(
            "/api/admin/config/message-limit", new { perMessageCharacterCap = 100, dailyMessageCap = (int?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NonAdmin_CannotWrite_PersonaGenerationModelConfig()
    {
        var response = await NonAdminClient().PutAsJsonAsync(
            "/api/admin/config/persona-generation-model", new { allowedModelIds = new[] { "azure-foundry:gpt-5" } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FreshTenant_MessageLimitRead_ReturnsDefaults_NotAnError()
    {
        // A brand-new factory/database with no admin write yet — Edge Case default behavior.
        using var freshFactory = new ModelAccessWebApplicationFactory();
        var client = freshFactory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "dave@contoso.com");

        var response = await client.GetAsync("/api/config/message-limit");
        var body = await response.Content.ReadFromJsonAsync<MessageLimitDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(body!.PerMessageCharacterCap);
        Assert.Null(body.DailyMessageCap);
    }

    private sealed record MessageLimitDto(int? PerMessageCharacterCap, int? DailyMessageCap);
}
