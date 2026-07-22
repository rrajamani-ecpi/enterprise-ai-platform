using System.Reflection;
using EnterpriseAIPlatform.Application.Authorization;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Infrastructure.Authentication;
using Xunit;

namespace EnterpriseAIPlatform.ArchitectureTests;

/// <summary>
/// Spec 002 SC-002 / Constitution Principle IV: exactly one implementation per concern.
/// Guards against a second, divergent downgrade/resolver/current-user implementation
/// silently reappearing.
/// </summary>
public class SingleImplementationTests
{
    private static readonly Assembly[] PlatformAssemblies =
    {
        typeof(RoleResolver).Assembly,   // Infrastructure
        typeof(IRoleResolver).Assembly,  // Application
    };

    private static List<Type> ConcreteImplementationsOf<T>() =>
        PlatformAssemblies
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(T).IsAssignableFrom(t))
            .ToList();

    [Fact]
    public void ExactlyOne_ICurrentUserAccessor_Implementation()
    {
        Assert.Single(ConcreteImplementationsOf<ICurrentUserAccessor>());
    }

    [Fact]
    public void ExactlyOne_IRoleResolver_Implementation()
    {
        Assert.Single(ConcreteImplementationsOf<IRoleResolver>());
    }

    [Fact]
    public void ExactlyOne_IIdentityHasher_Implementation()
    {
        Assert.Single(ConcreteImplementationsOf<IIdentityHasher>());
    }

    [Fact]
    public void RoleDowngrade_IsTheSingleStaticImplementation()
    {
        var type = typeof(RoleDowngrade);

        // A C# static class compiles to abstract + sealed.
        Assert.True(type is { IsAbstract: true, IsSealed: true });
    }
}
