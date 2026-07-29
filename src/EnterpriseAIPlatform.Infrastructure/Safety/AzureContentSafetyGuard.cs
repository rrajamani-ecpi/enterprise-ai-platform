using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Domain.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnterpriseAIPlatform.Infrastructure.Safety;

/// <summary>
/// The single implementation of the Content Safety guardrail (spec 004 D7). Authenticates via
/// workload identity — no API key anywhere on this type or in <see cref="ContentSafetyOptions"/>.
/// If unconfigured, allows through with a logged warning; startup validation (<c>Program.cs</c>)
/// already prevents this type from ever seeing an unconfigured endpoint in Production.
/// </summary>
public sealed class AzureContentSafetyGuard : IContentSafetyGuard
{
    private static readonly string[] Scopes = { "https://cognitiveservices.azure.com/.default" };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ContentSafetyOptions _options;
    private readonly ILogger<AzureContentSafetyGuard> _logger;
    private readonly TokenCredential _credential;

    public AzureContentSafetyGuard(
        HttpClient httpClient,
        IOptions<ContentSafetyOptions> options,
        ILogger<AzureContentSafetyGuard> logger,
        TokenCredential? credential = null)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _credential = credential ?? new DefaultAzureCredential();
    }

    public async Task<ContentSafetyVerdict> CheckAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            _logger.LogWarning(
                "ContentSafety:Endpoint is not configured; allowing the message through. This is only " +
                "permitted because startup validation (Program.cs) already blocks this in Production.");
            return ContentSafetyVerdict.Allowed();
        }

        var token = await _credential.GetTokenAsync(new TokenRequestContext(Scopes), cancellationToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{_options.Endpoint.TrimEnd('/')}/contentsafety/text:analyze?api-version=2024-09-01")
        {
            Content = JsonContent.Create(new { text }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ContentSafetyApiResponse>(JsonOptions, cancellationToken);
        var flagged = payload?.CategoriesAnalysis?.FirstOrDefault(c => c.Severity > 0);

        return flagged is not null ? ContentSafetyVerdict.Blocked(flagged.Category) : ContentSafetyVerdict.Allowed();
    }

    private sealed record ContentSafetyApiResponse(List<CategoryAnalysis>? CategoriesAnalysis);

    private sealed record CategoryAnalysis(string Category, int Severity);
}
