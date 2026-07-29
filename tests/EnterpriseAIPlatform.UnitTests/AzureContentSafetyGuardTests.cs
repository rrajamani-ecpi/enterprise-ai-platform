using System.Net;
using System.Net.Http.Json;
using Azure.Core;
using EnterpriseAIPlatform.Infrastructure.Safety;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>Spec 004 D7: the Content Safety guardrail — blocks unsafe text, allows safe text, allows through (with a warning) when unconfigured.</summary>
public class AzureContentSafetyGuardTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly object _payload;

        public FakeHandler(object payload) => _payload = payload;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(_payload) });
    }

    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("fake-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private static AzureContentSafetyGuard BuildGuard(object apiPayload, string? endpoint = "https://safety.example.com")
    {
        var httpClient = new HttpClient(new FakeHandler(apiPayload));
        var options = Options.Create(new ContentSafetyOptions { Endpoint = endpoint });
        return new AzureContentSafetyGuard(httpClient, options, NullLogger<AzureContentSafetyGuard>.Instance, new FakeTokenCredential());
    }

    [Fact]
    public async Task CheckAsync_Allows_WhenNoCategoryIsFlagged()
    {
        var guard = BuildGuard(new { categoriesAnalysis = new[] { new { category = "Hate", severity = 0 } } });

        var verdict = await guard.CheckAsync("hello there");

        Assert.True(verdict.IsAllowed);
    }

    [Fact]
    public async Task CheckAsync_Blocks_WhenACategoryIsFlagged()
    {
        var guard = BuildGuard(new { categoriesAnalysis = new[] { new { category = "Hate", severity = 4 } } });

        var verdict = await guard.CheckAsync("something unsafe");

        Assert.False(verdict.IsAllowed);
        Assert.Equal("Hate", verdict.Category);
    }

    [Fact]
    public async Task CheckAsync_UnconfiguredEndpoint_AllowsThroughWithoutCallingTheApi()
    {
        var guard = BuildGuard(new { }, endpoint: null);

        var verdict = await guard.CheckAsync("anything");

        Assert.True(verdict.IsAllowed);
    }
}
