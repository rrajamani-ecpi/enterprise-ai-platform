using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.ModelAccess;
using EnterpriseAIPlatform.Domain.Prompts;
using EnterpriseAIPlatform.Infrastructure.Prompts;
using NSubstitute;

namespace EnterpriseAIPlatform.UnitTests.Prompts;

/// <summary>
/// Spec 016 FR-010 — the generation fallback is a single extra attempt, not a retry loop, and the
/// allow-list is a hard validity set rather than advisory. These run without a host so the
/// attempt-counting assertions are exact.
/// </summary>
public class PromptGenerationFallbackTests
{
    private const string Primary = "openai:gpt-primary";
    private const string Fallback = "openai:gpt-fallback";

    private readonly IPersonaGenerationModelConfigService _config = Substitute.For<IPersonaGenerationModelConfigService>();
    private readonly IModelCatalogService _catalog = Substitute.For<IModelCatalogService>();
    private readonly CountingCompletionClient _client = new();
    private readonly UserModel _caller = new() { Name = "Alice", Email = "alice@contoso.com" };

    public PromptGenerationFallbackTests()
    {
        foreach (var id in new[] { Primary, Fallback })
        {
            _catalog.GetAsync(id, false, Arg.Any<CancellationToken>())
                .Returns(ServerActionResponse<ModelConfigDocument>.Ok(new ModelConfigDocument
                {
                    Id = id,
                    DisplayName = id,
                    Provider = "openai",
                    IsEnabled = true,
                }));
        }
    }

    private void Configure(string? primary, string? fallback, IEnumerable<string>? allowed = null) =>
        _config.GetAsync(Arg.Any<CancellationToken>()).Returns(
            ServerActionResponse<PersonaGenerationModelConfig>.Ok(new PersonaGenerationModelConfig
            {
                AllowedModelIds = (allowed ?? new[] { Primary, Fallback }).ToList(),
                PrimaryModelId = primary,
                FallbackModelId = fallback,
            }));

    private PromptGenerationService CreateService() =>
        new(_config, _catalog, new IChatCompletionClient[] { _client });

    private Task<ServerActionResponse<PromptGenerationResult>> GenerateAsync() =>
        CreateService().GenerateAsync(new PromptGenerationRequest("summarize meeting notes"), _caller);

    [Fact]
    public async Task PrimarySuccess_NeverInvokesFallback()
    {
        Configure(Primary, Fallback);
        _client.Behaviors[Primary] = () => "Summarize the following meeting notes.";

        var result = await GenerateAsync();

        Assert.Equal(ResponseStatus.OK, result.Status);
        Assert.Equal("Summarize the following meeting notes.", result.Response!.GeneratedText);
        Assert.Equal(Primary, result.Response.ModelId);
        Assert.False(result.Response.UsedFallback);
        Assert.Equal(new[] { Primary }, _client.Attempts);
    }

    [Fact]
    public async Task PrimaryFails_FallbackIsAttemptedExactlyOnce()
    {
        Configure(Primary, Fallback);
        _client.Behaviors[Primary] = () => throw new InvalidOperationException("primary down");
        _client.Behaviors[Fallback] = () => "Summarize the following meeting notes.";

        var result = await GenerateAsync();

        Assert.Equal(ResponseStatus.OK, result.Status);
        Assert.Equal(Fallback, result.Response!.ModelId);
        Assert.True(result.Response.UsedFallback);
        Assert.Equal(new[] { Primary, Fallback }, _client.Attempts);
    }

    [Fact]
    public async Task BothFail_StopsAfterOneFallbackAttempt()
    {
        // The bound that makes FR-010 falsifiable: no retry loop hides behind "falls back".
        Configure(Primary, Fallback);
        _client.Behaviors[Primary] = () => throw new InvalidOperationException("primary down");
        _client.Behaviors[Fallback] = () => throw new InvalidOperationException("fallback down");

        var result = await GenerateAsync();

        Assert.Equal(ResponseStatus.ERROR, result.Status);
        Assert.Equal(new[] { Primary, Fallback }, _client.Attempts);
    }

    [Fact]
    public async Task PrimaryFails_WithNoFallbackConfigured_MakesNoSecondAttempt()
    {
        Configure(Primary, fallback: null, allowed: new[] { Primary });
        _client.Behaviors[Primary] = () => throw new InvalidOperationException("primary down");

        var result = await GenerateAsync();

        Assert.Equal(ResponseStatus.ERROR, result.Status);
        Assert.Equal(PromptGenerationService.GenerationFailedMessage, result.Errors[0].Message);
        Assert.Equal(new[] { Primary }, _client.Attempts);
    }

    [Fact]
    public async Task PrimaryOutsideAllowedModelIds_IsAConfigurationError_NotAModelCall()
    {
        Configure(Primary, Fallback, allowed: new[] { Fallback });
        _client.Behaviors[Primary] = () => "should never be reached";

        var result = await GenerateAsync();

        Assert.Equal(ResponseStatus.ERROR, result.Status);
        Assert.Equal(PromptGenerationService.OutOfSetMessage(Primary, "primary"), result.Errors[0].Message);
        Assert.Empty(_client.Attempts);
    }

    [Fact]
    public async Task FallbackOutsideAllowedModelIds_IsRejectedBeforeAnyAttempt()
    {
        // Rejected up front rather than on primary failure, so the misconfiguration surfaces even
        // when the primary happens to be healthy.
        Configure(Primary, Fallback, allowed: new[] { Primary });
        _client.Behaviors[Primary] = () => "should never be reached";

        var result = await GenerateAsync();

        Assert.Equal(ResponseStatus.ERROR, result.Status);
        Assert.Equal(PromptGenerationService.OutOfSetMessage(Fallback, "fallback"), result.Errors[0].Message);
        Assert.Empty(_client.Attempts);
    }

    [Fact]
    public async Task NoPrimaryConfigured_FailsClosed()
    {
        Configure(primary: null, fallback: Fallback);

        var result = await GenerateAsync();

        Assert.Equal(ResponseStatus.ERROR, result.Status);
        Assert.Equal(PromptGenerationService.NoPrimaryConfiguredMessage, result.Errors[0].Message);
        Assert.Empty(_client.Attempts);
    }

    [Fact]
    public async Task EmptyModelOutput_CountsAsAFailure_AndFallsBack()
    {
        // Principle III: an empty stream is not an empty success.
        Configure(Primary, Fallback);
        _client.Behaviors[Primary] = () => "   ";
        _client.Behaviors[Fallback] = () => "Summarize the following meeting notes.";

        var result = await GenerateAsync();

        Assert.Equal(ResponseStatus.OK, result.Status);
        Assert.True(result.Response!.UsedFallback);
        Assert.Equal(new[] { Primary, Fallback }, _client.Attempts);
    }

    [Fact]
    public async Task EmptyIntent_IsRejectedWithoutCallingAModel()
    {
        Configure(Primary, Fallback);

        var result = await CreateService().GenerateAsync(new PromptGenerationRequest("   "), _caller);

        Assert.Equal(ResponseStatus.ERROR, result.Status);
        Assert.Equal(PromptGenerationService.EmptyIntentMessage, result.Errors[0].Message);
        Assert.Empty(_client.Attempts);
    }

    [Fact]
    public async Task UserIntent_IsWrappedInTheFixedMetaPrompt()
    {
        Configure(Primary, Fallback);
        _client.Behaviors[Primary] = () => "generated";

        await GenerateAsync();

        var sent = Assert.Single(_client.Requests).Messages.Single().Content;
        Assert.StartsWith(PromptGenerationService.MetaPrompt, sent);
        Assert.EndsWith("summarize meeting notes", sent);
    }

    /// <summary>
    /// Records every model actually invoked. A substitute would let a stray extra attempt pass
    /// unnoticed; an explicit attempt log makes "exactly once" an assertion rather than a hope.
    /// </summary>
    private sealed class CountingCompletionClient : IChatCompletionClient
    {
        public string Provider => "openai";

        public List<string> Attempts { get; } = new();

        public List<ChatRequest> Requests { get; } = new();

        public Dictionary<string, Func<string>> Behaviors { get; } = new();

        public async IAsyncEnumerable<string> StreamCompletionAsync(
            ChatRequest request,
            ModelConfigDocument model,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Attempts.Add(model.Id);
            Requests.Add(request);

            var text = Behaviors.TryGetValue(model.Id, out var behavior)
                ? behavior()
                : throw new InvalidOperationException($"No behavior configured for '{model.Id}'.");

            // Chunked to prove the service accumulates rather than taking only the first chunk.
            foreach (var chunk in text.Chunk(4))
            {
                await Task.Yield();
                yield return new string(chunk);
            }
        }
    }
}
