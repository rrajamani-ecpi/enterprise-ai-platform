using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.ModelAccess;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseAIPlatform.IntegrationTests.Prompts;

/// <summary>
/// Spec 016 US3 / SC-003 — a total generation failure must come back as JSON matching the success
/// path's content type. The legacy defect this closes was a plain-text 500 body, which made the
/// client's JSON parse throw and hid the real cause. Asserted end-to-end through the real
/// <c>PromptGenerationService</c>, with only the model boundary faked, so the endpoint's error
/// rendering is exercised rather than a stubbed service's.
/// </summary>
public class PromptGenerationFailureTests : IClassFixture<PromptWebApplicationFactory>
{
    private const string Primary = "test-fail:primary";
    private const string Fallback = "test-fail:fallback";
    private const string Provider = "test-fail";

    private readonly PromptWebApplicationFactory _factory;

    public PromptGenerationFailureTests(PromptWebApplicationFactory factory) => _factory = factory;

    /// <summary>
    /// Replaces only the model boundary: the generation allow-list/selection, the catalog lookup,
    /// and the completion clients. Everything else — routing, auth, the service itself, and the
    /// response envelope — is the production wiring.
    /// </summary>
    private HttpClient CreateClient(
        string? fallbackModelId,
        bool catalogResolves,
        Func<string>? behavior = null)
    {
        var configured = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddScoped<IPersonaGenerationModelConfigService>(
                _ => new FakeGenerationModelConfigService(fallbackModelId));
            services.AddScoped<IModelCatalogService>(_ => new FakeModelCatalogService(catalogResolves));
            services.AddSingleton<IChatCompletionClient>(
                new ScriptedCompletionClient(behavior ?? (() => throw new InvalidOperationException("model unavailable"))));
        }));

        var client = configured.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "alice@contoso.com");
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "Employee");
        return client;
    }

    private static Task<HttpResponseMessage> GenerateAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/promptGenerator", new { Intent = "summarize meeting notes" });

    [Fact]
    public async Task PrimaryFails_WithNoFallbackConfigured_ReturnsJson()
    {
        using var client = CreateClient(fallbackModelId: null, catalogResolves: true);

        var response = await GenerateAsync(client);

        Assert.False(response.IsSuccessStatusCode);
        await AssertStructuredJsonErrorAsync(response);
    }

    [Fact]
    public async Task PrimaryAndFallbackBothFail_ReturnsJson()
    {
        using var client = CreateClient(Fallback, catalogResolves: true);

        var response = await GenerateAsync(client);

        Assert.False(response.IsSuccessStatusCode);
        await AssertStructuredJsonErrorAsync(response);
    }

    [Fact]
    public async Task ModelCannotBeResolved_ReturnsJson()
    {
        // A different failure shape (catalog miss rather than a throwing client) reaching the same
        // envelope — the response format must not depend on which layer failed.
        using var client = CreateClient(Fallback, catalogResolves: false);

        var response = await GenerateAsync(client);

        Assert.False(response.IsSuccessStatusCode);
        await AssertStructuredJsonErrorAsync(response);
    }

    [Fact]
    public async Task FailureContentType_MatchesTheSuccessPath()
    {
        using var successClient = CreateClient(Fallback, catalogResolves: true, behavior: () => "Summarize the notes below.");
        using var failureClient = CreateClient(Fallback, catalogResolves: true);

        var success = await GenerateAsync(successClient);
        var failure = await GenerateAsync(failureClient);

        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.False(failure.IsSuccessStatusCode);
        Assert.Equal(
            success.Content.Headers.ContentType?.MediaType,
            failure.Content.Headers.ContentType?.MediaType);
        Assert.Equal("application/json", failure.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Failure_IsNotASuccessShapedBodyCarryingAnErrorAsGeneratedText()
    {
        // Principle III: the caller must be able to tell "generation failed" from "the model
        // generated the words 'generation failed'".
        using var client = CreateClient(Fallback, catalogResolves: true);

        var response = await GenerateAsync(client);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.False(document.RootElement.TryGetProperty("generatedText", out _));
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AssertStructuredJsonErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        var errors = document.RootElement.GetProperty("errors");
        Assert.Equal(JsonValueKind.Array, errors.ValueKind);
        var message = errors[0].GetProperty("message").GetString();
        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    private sealed class FakeGenerationModelConfigService : IPersonaGenerationModelConfigService
    {
        private readonly string? _fallbackModelId;

        public FakeGenerationModelConfigService(string? fallbackModelId) => _fallbackModelId = fallbackModelId;

        public Task<ServerActionResponse<PersonaGenerationModelConfig>> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ServerActionResponse<PersonaGenerationModelConfig>.Ok(new PersonaGenerationModelConfig
            {
                AllowedModelIds = new List<string> { Primary, Fallback },
                PrimaryModelId = Primary,
                FallbackModelId = _fallbackModelId,
            }));

        public Task<ServerActionResponse<bool>> SetAllowedModelsAsync(
            IReadOnlyList<string> modelIds, string updatedByUserId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServerActionResponse<bool>> ValidateSelectionAsync(
            string modelId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeModelCatalogService : IModelCatalogService
    {
        private readonly bool _resolves;

        public FakeModelCatalogService(bool resolves) => _resolves = resolves;

        public Task<ServerActionResponse<ModelConfigDocument>> GetAsync(
            string canonicalId, bool includeDeleted = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(_resolves
                ? ServerActionResponse<ModelConfigDocument>.Ok(new ModelConfigDocument
                {
                    Id = canonicalId,
                    DisplayName = canonicalId,
                    Provider = PromptGenerationFailureTests.Provider,
                    IsEnabled = true,
                })
                : ServerActionResponse<ModelConfigDocument>.Error($"'{canonicalId}' is unavailable."));

        public Task<ServerActionResponse<IReadOnlyList<ModelConfigDocument>>> ListAsync(
            bool includeDeleted = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServerActionResponse<bool>> UpsertAsync(
            ModelConfigDocument model, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServerActionResponse<bool>> SoftDeleteAsync(
            string canonicalId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServerActionResponse<string>> ResolveAliasAsync(
            string canonicalId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ScriptedCompletionClient : IChatCompletionClient
    {
        private readonly Func<string> _behavior;

        public ScriptedCompletionClient(Func<string> behavior) => _behavior = behavior;

        public string Provider => PromptGenerationFailureTests.Provider;

        public async IAsyncEnumerable<string> StreamCompletionAsync(
            ChatRequest request,
            ModelConfigDocument model,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var text = _behavior();
            await Task.Yield();
            yield return text;
        }
    }
}
