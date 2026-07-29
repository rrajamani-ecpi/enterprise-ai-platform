using Azure.Core;
using Azure.Identity;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.ModelAccess;

namespace EnterpriseAIPlatform.Infrastructure.ModelProviders;

/// <summary>
/// The R1 <see cref="IModelProviderAdapter"/> implementation for Azure/Foundry-hosted models
/// (spec 014 FR-012/FR-013, US7/US8). Authenticates via <see cref="DefaultAzureCredential"/>
/// (workload identity) — there is no API-key/secret field anywhere on this type or in its
/// configuration (<see cref="AzureFoundryOptions"/>). Only adapts request/response shapes; the
/// actual HTTP dispatch to the Foundry endpoint is spec 004's chat-pipeline concern.
/// </summary>
public sealed class AzureFoundryProviderAdapter : IModelProviderAdapter
{
    public const string ProviderKey = "azure-foundry";

    private static readonly string[] Scopes = { "https://cognitiveservices.azure.com/.default" };

    private readonly TokenCredential _credential;

    public AzureFoundryProviderAdapter(TokenCredential? credential = null) =>
        _credential = credential ?? new DefaultAzureCredential();

    public string Provider => ProviderKey;

    public async Task<ProviderRequest> AdaptRequestAsync(
        ChatRequest request, ModelConfigDocument model, CancellationToken cancellationToken = default)
    {
        var token = await _credential.GetTokenAsync(new TokenRequestContext(Scopes), cancellationToken);

        // Foundry Responses API shape: role/content map through unchanged; no provider-only
        // fields from the caller's ChatRequest are forwarded (FR-012).
        var payload = new Dictionary<string, object?>
        {
            ["model"] = ModelIdWithoutProviderPrefix(model.Id),
            ["input"] = request.Messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
        };

        var headers = new Dictionary<string, string>
        {
            // A short-lived workload-identity token, never a static key (FR-013).
            ["Authorization"] = $"Bearer {token.Token}",
        };

        return new ProviderRequest(Provider, payload, headers);
    }

    public Task<ChatResponse> AdaptResponseAsync(
        ProviderResponse response, ModelConfigDocument model, CancellationToken cancellationToken = default)
    {
        if (response.IsError
            || !response.Payload.TryGetValue("output_text", out var text)
            || text is not string content)
        {
            // Normalize any provider-specific error/unexpected shape into one consistent shape
            // rather than leaking it to the caller (Edge Cases).
            return Task.FromResult(new ChatResponse(
                string.Empty, IsError: true, ErrorMessage: "The model provider returned an unexpected or error response."));
        }

        return Task.FromResult(new ChatResponse(content, IsError: false));
    }

    private static string ModelIdWithoutProviderPrefix(string canonicalId)
    {
        var parts = canonicalId.Split(':', 2);
        return parts.Length == 2 ? parts[1] : canonicalId;
    }
}
