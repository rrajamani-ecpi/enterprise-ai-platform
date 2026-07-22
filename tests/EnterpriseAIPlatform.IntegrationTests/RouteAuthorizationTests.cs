using System.Net;
using Xunit;

namespace EnterpriseAIPlatform.IntegrationTests;

/// <summary>
/// Spec 002 US5 / FR-011/012/013 / SC-006/007: deny-by-default route protection with an explicit
/// public allow-list, and server-side admin gating that a client cannot bypass.
/// </summary>
public class RouteAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public RouteAuthorizationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    [Fact]
    public async Task PublicHealthRoute_Anonymous_IsServed()
    {
        var response = await CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedRoute_Anonymous_IsDenied()
    {
        var response = await CreateClient().GetAsync("/api/whoami");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedRoute_Authenticated_IsServed()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "alice@contoso.com");

        var response = await client.GetAsync("/api/whoami");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminRoute_NonAdmin_IsForbidden()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "bob@contoso.com");

        var response = await client.GetAsync("/api/admin/ping");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminRoute_Admin_IsServed()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "admin@contoso.com");
        client.DefaultRequestHeaders.Add(TestAuthHandler.AdminHeader, "true");

        var response = await client.GetAsync("/api/admin/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
