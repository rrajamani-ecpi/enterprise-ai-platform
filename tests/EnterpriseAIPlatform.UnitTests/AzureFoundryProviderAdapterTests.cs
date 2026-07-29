using System.Reflection;
using Azure.Core;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.ModelAccess;
using EnterpriseAIPlatform.Infrastructure.ModelProviders;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>Spec 014 US8 / FR-013 / SC-009: workload identity auth, never a static provider secret.</summary>
public class AzureFoundryProviderAdapterTests
{
    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("fake-workload-identity-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private static ModelConfigDocument Model() => new()
    {
        Id = "azure-foundry:gpt-5",
        DisplayName = "GPT-5",
        Provider = AzureFoundryProviderAdapter.ProviderKey,
        IsEnabled = true,
        SupportsToolCalling = true,
        SupportsVision = true,
        SupportsReasoning = true,
        AccessTier = ModelAccessTier.Standard,
    };

    [Fact]
    public async Task AdaptRequestAsync_AuthenticatesViaTokenCredential_NotAStaticKey()
    {
        var adapter = new AzureFoundryProviderAdapter(new FakeTokenCredential());
        var request = new ChatRequest(new[] { new ChatMessage("user", "hello") });

        var providerRequest = await adapter.AdaptRequestAsync(request, Model());

        Assert.Equal("Bearer fake-workload-identity-token", providerRequest.Headers?["Authorization"]);
        Assert.DoesNotContain("input", providerRequest.Headers?.Keys ?? Array.Empty<string>());
    }

    [Fact]
    public void AdapterType_HasNoStaticApiKeyOrSecretField()
    {
        // "ProviderKey" (the provider identifier, e.g. "azure-foundry") is intentionally excluded
        // — it names a routing key, not a credential. "ApiKey"/"Secret"/"ClientSecret" would not be.
        var suspiciousMembers = typeof(AzureFoundryProviderAdapter)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .Where(f => f.Name.Contains("apikey", StringComparison.OrdinalIgnoreCase)
                        || f.Name.Contains("secret", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(suspiciousMembers);
    }

    [Fact]
    public async Task AdaptResponseAsync_NormalizesProviderError_IntoConsistentShape()
    {
        var adapter = new AzureFoundryProviderAdapter(new FakeTokenCredential());
        var errorResponse = new ProviderResponse(
            AzureFoundryProviderAdapter.ProviderKey,
            new Dictionary<string, object?> { ["unexpected_shape"] = true },
            IsError: true);

        var result = await adapter.AdaptResponseAsync(errorResponse, Model());

        Assert.True(result.IsError);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void ClientDeliveredConfig_ContainsNoLongLivedProviderSecret()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var appSettingsPath = Path.Combine(repoRoot, "src", "EnterpriseAIPlatform.Web", "appsettings.json");
        var devAppSettingsPath = Path.Combine(repoRoot, "src", "EnterpriseAIPlatform.Web", "appsettings.Development.json");

        foreach (var path in new[] { appSettingsPath, devAppSettingsPath })
        {
            var content = File.ReadAllText(path);
            Assert.DoesNotContain("apikey", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("api_key", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("clientsecret", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EnterpriseAIPlatform.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root (EnterpriseAIPlatform.slnx) from " + startDirectory);
    }
}
