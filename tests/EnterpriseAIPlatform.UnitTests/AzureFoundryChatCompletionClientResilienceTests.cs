using EnterpriseAIPlatform.Infrastructure.DependencyInjection;
using EnterpriseAIPlatform.Infrastructure.ModelProviders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using NSubstitute;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>
/// Spec 004 D8 (constitution WAF Reliability pillar): the outbound HTTP call inside
/// <see cref="AzureFoundryChatCompletionClient"/> is wrapped in a
/// <c>Microsoft.Extensions.Http.Resilience</c> standard resilience handler (timeout/retry/circuit
/// breaker), extending FR-009's tool-call pattern to the model call itself.
/// </summary>
public class AzureFoundryChatCompletionClientResilienceTests
{
    [Fact]
    public void TypedHttpClient_HandlerChain_IncludesAResilienceHandler()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Development);
        services.AddSingleton(environment);
        services.AddChatInfrastructure(configuration);

        var provider = services.BuildServiceProvider();
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(nameof(AzureFoundryChatCompletionClient));

        var chain = new List<string>();
        var current = handler;
        while (current is DelegatingHandler delegating)
        {
            chain.Add(delegating.GetType().FullName ?? delegating.GetType().Name);
            current = delegating.InnerHandler;
        }

        Assert.Contains(chain, name => name.Contains("Resilience", StringComparison.OrdinalIgnoreCase));
    }
}
