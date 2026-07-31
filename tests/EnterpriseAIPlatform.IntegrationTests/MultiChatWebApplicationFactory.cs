using EnterpriseAIPlatform.Application.Chat;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EnterpriseAIPlatform.IntegrationTests;

/// <summary>
/// Spec 006 integration test host. Extends <see cref="ChatWebApplicationFactory"/>'s fakes with an
/// in-memory <see cref="IMultiChatSessionStore"/> — no live Cosmos dependency in tests.
/// </summary>
public sealed class MultiChatWebApplicationFactory : ChatWebApplicationFactory
{
    public FakeMultiChatSessionStore SessionStore { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMultiChatSessionStore>();
            services.AddSingleton<IMultiChatSessionStore>(SessionStore);
        });
    }
}
