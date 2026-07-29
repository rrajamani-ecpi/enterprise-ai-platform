using EnterpriseAIPlatform.Infrastructure.DependencyInjection;
using EnterpriseAIPlatform.Infrastructure.Safety;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>
/// Spec 004 D7: <see cref="ContentSafetyOptions"/> validation (registered in
/// <see cref="ChatServiceCollectionExtensions.AddChatInfrastructure"/>) — unconfigured in
/// Production fails startup; unconfigured in Development is permitted.
/// </summary>
public class ContentSafetyOptionsValidationTests
{
    private static IServiceProvider BuildProvider(string environmentName)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();

        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        services.AddSingleton(environment);

        services.AddChatInfrastructure(configuration);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void UnconfiguredEndpoint_InProduction_FailsValidation()
    {
        var provider = BuildProvider(Environments.Production);

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ContentSafetyOptions>>().Value);
    }

    [Fact]
    public void UnconfiguredEndpoint_InDevelopment_PassesValidation()
    {
        var provider = BuildProvider(Environments.Development);

        var options = provider.GetRequiredService<IOptions<ContentSafetyOptions>>().Value;

        Assert.True(string.IsNullOrWhiteSpace(options.Endpoint));
    }
}
