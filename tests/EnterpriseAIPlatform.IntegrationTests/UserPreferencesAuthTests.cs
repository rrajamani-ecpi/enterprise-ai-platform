using System.Net;
using System.Net.Http.Json;

namespace EnterpriseAIPlatform.IntegrationTests;

/// <summary>Spec 014 US5 / FR-010 / SC-006: /api/user/preferences/* returns 401, never 500, for an unauthenticated caller.</summary>
public sealed class UserPreferencesAuthTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public UserPreferencesAuthTests(CustomWebApplicationFactory factory) => _factory = factory;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task NoSession_Returns401_NeverServerError(string method)
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(new HttpMethod(method), "/api/user/preferences");
        if (method == "PUT")
        {
            request.Content = JsonContent.Create(new { theme = "dark" });
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True((int)response.StatusCode < 500);
    }

    [Fact]
    public async Task ActiveSession_Get_IsUnaffected()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "frank@contoso.com");

        var response = await client.GetAsync("/api/user/preferences");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ActiveSession_Put_IsUnaffected()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "frank@contoso.com");

        var response = await client.PutAsJsonAsync("/api/user/preferences", new { theme = "dark" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
