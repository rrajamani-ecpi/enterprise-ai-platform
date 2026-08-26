using System.Net;
using System.Text.Json;
using EnterpriseAIPlatform.Application.Chat;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EnterpriseAIPlatform.IntegrationTests;

public sealed class DevelopmentAuthenticationTests
{
    [Fact]
    public async Task DevelopmentMode_AuthenticatesConfiguredUser_AndPreservesServerAuthorization()
    {
        await using var factory = new DevelopmentAuthenticationFactory();
        var client = factory.CreateClient();

        var whoAmIResponse = await client.GetAsync("/api/whoami");
        var adminResponse = await client.GetAsync("/api/admin/ping");

        Assert.Equal(HttpStatusCode.OK, whoAmIResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);

        using var body = JsonDocument.Parse(await whoAmIResponse.Content.ReadAsStringAsync());
        Assert.Equal("dev.user@example.test", body.RootElement.GetProperty("email").GetString());
        Assert.True(body.RootElement.GetProperty("isAdmin").GetBoolean());
    }

    [Fact]
    public async Task DevelopmentMode_ShowsVisibleWarningInUi()
    {
        await using var factory = new DevelopmentAuthenticationFactory();
        var html = await factory.CreateClient().GetStringAsync("/");

        Assert.Contains("Development authentication is active", html);
        Assert.Contains("dev.user@example.test", html);
    }

    [Fact]
    public void DevelopmentMode_IsRejectedOutsideDevelopmentEnvironment()
    {
        using var factory = new ProductionDevelopmentAuthenticationFactory();

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Development authentication is enabled outside", exception.ToString());
    }

    private sealed class DevelopmentAuthenticationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("PlatformAuthentication:Mode", "Development");
            builder.UseSetting("PlatformAuthentication:DevelopmentUser:Name", "Development User");
            builder.UseSetting("PlatformAuthentication:DevelopmentUser:Email", "dev.user@example.test");
            builder.UseSetting("PlatformAuthentication:DevelopmentUser:IsAdmin", "true");
            builder.UseSetting("PlatformAuthentication:DevelopmentUser:IsEmployee", "true");

            // Rendering "/" now loads the spec 024 US3 conversation sidebar, which needs
            // IChatThreadStore — swap in the fake so this test doesn't need a live Cosmos DB.
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IChatThreadStore>();
                services.AddSingleton<IChatThreadStore>(new FakeChatThreadStore());
            });
        }
    }

    private sealed class ProductionDevelopmentAuthenticationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("PlatformAuthentication:Mode", "Development");
        }
    }
}
