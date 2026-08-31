using EnterpriseAIPlatform.Application.Authorization;
using EnterpriseAIPlatform.Application.Sharing;
using EnterpriseAIPlatform.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EnterpriseAIPlatform.UnitTests.Sharing;

/// <summary>
/// Spec 018 T033, Constitution Principle III (fail loud): a malformed <c>RoleSharing</c> section
/// fails on first access, never silently. Exercises the real <c>AddSharingInfrastructure</c> DI
/// wiring end-to-end, without a full host.
/// </summary>
public class SharingServiceCollectionExtensionsTests
{
    [Fact]
    public void AdminKeyInConfig_ThrowsOnFirstAccess()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RoleSharing:Roles:Admin:GroupSharingEnabled"] = "true",
            })
            .Build();

        var provider = new ServiceCollection().AddSharingInfrastructure(configuration).BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<RoleSharingPolicyOptions>>().Value);
    }

    [Fact]
    public void ValidConfig_ResolvesWithoutThrowing_AndBindsCorrectly()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RoleSharing:Roles:Employee:GroupSharingEnabled"] = "true",
                ["RoleSharing:Roles:Employee:AllowedGroups:0"] = "faculty",
            })
            .Build();

        var provider = new ServiceCollection().AddSharingInfrastructure(configuration).BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<RoleSharingPolicyOptions>>().Value;

        Assert.True(options.Roles[RoleName.Employee].GroupSharingEnabled);
        Assert.Equal(new[] { "faculty" }, options.Roles[RoleName.Employee].AllowedGroups);
    }

    [Fact]
    public void RegistersExactlyOne_ISharingPolicyService()
    {
        var provider = new ServiceCollection()
            .AddSharingInfrastructure(new ConfigurationBuilder().Build())
            .BuildServiceProvider();

        var service = provider.GetRequiredService<ISharingPolicyService>();

        Assert.NotNull(service);
    }
}
