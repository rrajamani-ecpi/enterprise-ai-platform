using System.Net;
using System.Net.Http.Json;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.ModelAccess;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseAIPlatform.IntegrationTests;

/// <summary>Spec 006 US1/US2/US4: session persistence/restore, quadrant bounds, and the parallel-send endpoint.</summary>
public sealed class MultiChatSessionTests : IClassFixture<MultiChatWebApplicationFactory>
{
    private const string SeededModelId = "azure-foundry:gpt-5";
    private const string SecondModelId = "azure-foundry:gpt-5-mini";

    private readonly MultiChatWebApplicationFactory _factory;

    public MultiChatSessionTests(MultiChatWebApplicationFactory factory)
    {
        _factory = factory;
        SeedSecondModel();
    }

    private void SeedSecondModel()
    {
        using var scope = _factory.Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IModelCatalogService>();
        catalog.UpsertAsync(new ModelConfigDocument
        {
            Id = SecondModelId,
            DisplayName = "GPT-5 mini",
            Provider = "azure-foundry",
            IsEnabled = true,
            SupportsToolCalling = true,
            SupportsVision = true,
            SupportsReasoning = true,
            AccessTier = ModelAccessTier.Standard,
        }).GetAwaiter().GetResult();
    }

    private HttpClient CreateClient(string user) =>
        _factory.CreateClient().Also(c => c.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user));

    [Fact]
    public async Task GetSession_CreatesDefault_WithTwoQuadrants()
    {
        var client = CreateClient("mc-default@contoso.com");

        var session = await client.GetFromJsonAsync<SessionDto>("/api/multichat/session");

        Assert.Equal(2, session!.Quadrants.Count);
        Assert.All(session.Quadrants, q => Assert.Null(q.ModelId));
    }

    [Fact]
    public async Task AssignModel_ThenGetAgain_ReturnsIdenticalState()
    {
        var client = CreateClient("mc-restore@contoso.com");
        await client.GetFromJsonAsync<SessionDto>("/api/multichat/session");

        var assignResponse = await client.PutAsJsonAsync("/api/multichat/session/quadrants/0/model", new { modelId = SeededModelId });
        Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);

        var session = await client.GetFromJsonAsync<SessionDto>("/api/multichat/session");

        Assert.Equal(SeededModelId, session!.Quadrants.Single(q => q.Position == 0).ModelId);
    }

    [Fact]
    public async Task AddQuadrant_PastCap_IsRejected()
    {
        var client = CreateClient("mc-cap@contoso.com");
        await client.GetFromJsonAsync<SessionDto>("/api/multichat/session");

        await client.PostAsync("/api/multichat/session/quadrants", null); // 2 -> 3
        await client.PostAsync("/api/multichat/session/quadrants", null); // 3 -> 4
        var overCap = await client.PostAsync("/api/multichat/session/quadrants", null); // rejected

        Assert.Equal(HttpStatusCode.BadRequest, overCap.StatusCode);

        var session = await client.GetFromJsonAsync<SessionDto>("/api/multichat/session");
        Assert.Equal(4, session!.Quadrants.Count);
    }

    [Fact]
    public async Task RemoveQuadrant_AtFloor_ClearsAssignment_NeverDropsBelowTwo()
    {
        var client = CreateClient("mc-floor@contoso.com");
        await client.GetFromJsonAsync<SessionDto>("/api/multichat/session");
        await client.PutAsJsonAsync("/api/multichat/session/quadrants/1/model", new { modelId = SeededModelId });

        var response = await client.DeleteAsync("/api/multichat/session/quadrants");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var session = await client.GetFromJsonAsync<SessionDto>("/api/multichat/session");
        Assert.Equal(2, session!.Quadrants.Count);
        Assert.Null(session.Quadrants.Single(q => q.Position == 1).ModelId);
    }

    [Fact]
    public async Task ParallelSend_DispatchesToAllAssignedQuadrants_TaggedByPosition()
    {
        var client = CreateClient("mc-parallel@contoso.com");
        await client.GetFromJsonAsync<SessionDto>("/api/multichat/session");
        await client.PutAsJsonAsync("/api/multichat/session/quadrants/0/model", new { modelId = SeededModelId });
        await client.PutAsJsonAsync("/api/multichat/session/quadrants/1/model", new { modelId = SecondModelId });
        _factory.CompletionClient.Chunks = new[] { "hi" };

        var response = await client.PostAsJsonAsync("/api/multichat/session/messages", new { text = "compare these" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"position\":0", body);
        Assert.Contains("\"position\":1", body);
        Assert.Contains("\"kind\":\"done\"", body);
    }

    [Fact]
    public async Task ParallelSend_ToEmptyQuadrant_PersistsThreadImmediately()
    {
        var client = CreateClient("mc-ondemand@contoso.com");
        await client.GetFromJsonAsync<SessionDto>("/api/multichat/session");
        await client.PutAsJsonAsync("/api/multichat/session/quadrants/0/model", new { modelId = SeededModelId });

        var send = await client.PostAsJsonAsync("/api/multichat/session/messages", new { text = "hello" });
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        await send.Content.ReadAsStringAsync(); // ensure stream fully drains

        var session = await client.GetFromJsonAsync<SessionDto>("/api/multichat/session");
        Assert.NotNull(session!.Quadrants.Single(q => q.Position == 0).ThreadId);
    }

    [Fact]
    public async Task ParallelSend_OneQuadrantFailsToCreateThread_OthersStillSucceed()
    {
        var client = CreateClient("mc-error-isolation@contoso.com");
        await client.GetFromJsonAsync<SessionDto>("/api/multichat/session");
        await client.PutAsJsonAsync("/api/multichat/session/quadrants/0/model", new { modelId = SeededModelId });
        await client.PutAsJsonAsync("/api/multichat/session/quadrants/1/model", new { modelId = SecondModelId });
        _factory.ThreadStore.FailOnCreateForPositions.Add(0);
        _factory.CompletionClient.Chunks = new[] { "ok" };

        try
        {
            var response = await client.PostAsJsonAsync("/api/multichat/session/messages", new { text = "hello" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode); // never a generic 500 over one quadrant's failure
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"position\":0,\"kind\":\"error\"", body);
            Assert.Contains("\"position\":1,\"kind\":\"chunk\"", body);
        }
        finally
        {
            // Shared fixture across tests in this class — never leave the fake mutated for later tests.
            _factory.ThreadStore.FailOnCreateForPositions.Remove(0);
        }
    }

    private sealed record SessionDto(string Id, List<QuadrantDto> Quadrants);

    private sealed record QuadrantDto(int Position, string? PersonaId, string? ModelId, string? ThreadId);
}

internal static class HttpClientTestExtensions
{
    public static HttpClient Also(this HttpClient client, Action<HttpClient> configure)
    {
        configure(client);
        return client;
    }
}
