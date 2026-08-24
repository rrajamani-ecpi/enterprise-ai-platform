using System.Net;
using Xunit;

namespace EnterpriseAIPlatform.IntegrationTests.Web;

/// <summary>
/// Spec 024 US1 / FR-001: the chat home screen is reachable only when authenticated — enforced by
/// the same deny-by-default fallback policy spec 002's <see cref="RouteAuthorizationTests"/> already
/// covers for API routes, applied here to the Razor Components root route.
/// </summary>
public class ChatHomeAuthTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ChatHomeAuthTests(CustomWebApplicationFactory factory) => _factory = factory;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    [Fact]
    public async Task ChatHome_Anonymous_IsDenied_NeverRendersChatContent()
    {
        var response = await CreateClient().GetAsync("/");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("chat-home", body);
    }
}
