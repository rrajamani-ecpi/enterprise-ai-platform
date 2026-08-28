using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.ModelAccess;
using Microsoft.Extensions.Options;

namespace EnterpriseAIPlatform.Infrastructure.ModelProviders;

/// <summary>
/// The single R1 implementation of <see cref="IChatCompletionClient"/> (spec 004 D4/D8). Reuses
/// spec 014's <see cref="AzureFoundryProviderAdapter"/> for request/response adaptation and the
/// workload-identity auth header; the injected <see cref="HttpClient"/> is registered with a
/// <c>Microsoft.Extensions.Http.Resilience</c> standard resilience handler (timeout/retry/circuit
/// breaker) around this actual call (D8) — never invoked against a live endpoint in this
/// environment's test suite (D5), only its request-shaping/pipeline-wiring is unit-tested.
/// </summary>
public sealed class AzureFoundryChatCompletionClient : IChatCompletionClient
{
    private readonly HttpClient _httpClient;
    private readonly IModelProviderAdapter _adapter;
    private readonly AzureFoundryOptions _options;

    public AzureFoundryChatCompletionClient(
        HttpClient httpClient, IModelProviderAdapter adapter, IOptions<AzureFoundryOptions> options)
    {
        _httpClient = httpClient;
        _adapter = adapter;
        _options = options.Value;
    }

    public string Provider => AzureFoundryProviderAdapter.ProviderKey;

    public async IAsyncEnumerable<string> StreamCompletionAsync(
        ChatRequest request, ModelConfigDocument model, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var providerRequest = await _adapter.AdaptRequestAsync(request, model, cancellationToken);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUrl())
        {
            Content = JsonContent.Create(providerRequest.Payload),
        };

        foreach (var (key, value) in providerRequest.Headers ?? new Dictionary<string, string>())
        {
            httpRequest.Headers.TryAddWithoutValidation(key, value);
        }

        using var response = await _httpClient.SendAsync(
            httpRequest, HttpCompletionOption.ResponseContentRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        // The Responses API is called here without "stream": true, so the body is one complete
        // JSON object rather than SSE chunks — route it through the adapter's response contract
        // (the same contract non-streaming callers use) instead of yielding raw bytes/lines.
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, object?>>(cancellationToken)
            ?? new Dictionary<string, object?>();

        var chatResponse = await _adapter.AdaptResponseAsync(
            new ProviderResponse(Provider, payload), model, cancellationToken);

        if (chatResponse.IsError)
        {
            throw new InvalidOperationException(
                chatResponse.ErrorMessage ?? "The model provider returned an error.");
        }

        yield return chatResponse.Content;
    }

    private string BuildUrl() => $"{_options.Endpoint?.TrimEnd('/')}/openai/responses?api-version=2025-04-01-preview";
}
