using System.Net.Http.Headers;
using System.Net.Http.Json;
using EnterpriseAIPlatform.Application.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnterpriseAIPlatform.Infrastructure.Support;

/// <summary>
/// The single implementation of <see cref="IFeedbackForwarder"/> (spec 017 FR-009/010, D7). Never
/// throws to its caller — any failure (network, non-2xx, misconfiguration) is caught and logged.
/// </summary>
public sealed class EcpiFeedbackForwarder : IFeedbackForwarder
{
    private readonly HttpClient _httpClient;
    private readonly FeedbackOptions _options;
    private readonly ILogger<EcpiFeedbackForwarder> _logger;

    public EcpiFeedbackForwarder(HttpClient httpClient, IOptions<FeedbackOptions> options, ILogger<EcpiFeedbackForwarder> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> ForwardAsync(string threadId, string content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.EcpiApiEndpoint))
        {
            _logger.LogWarning("Feedback:EcpiApiEndpoint is not configured; feedback was not forwarded.");
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.EcpiApiEndpoint)
            {
                Content = JsonContent.Create(new { threadId, content }),
            };

            if (!string.IsNullOrWhiteSpace(_options.EcpiApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.EcpiApiKey);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Forwarding feedback to the ECPI API failed; the user was not shown an error.");
            return false;
        }
    }
}
