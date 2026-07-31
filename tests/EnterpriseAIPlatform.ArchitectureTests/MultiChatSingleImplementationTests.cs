using System.Reflection;
using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Infrastructure.Chat;
using Xunit;

namespace EnterpriseAIPlatform.ArchitectureTests;

/// <summary>Spec 006, Constitution Principle IV: exactly one implementation of the session store; the quadrant rules are a single static implementation.</summary>
public class MultiChatSingleImplementationTests
{
    private static readonly Assembly[] PlatformAssemblies =
    {
        typeof(CosmosMultiChatSessionStore).Assembly, // Infrastructure
        typeof(IMultiChatSessionStore).Assembly, // Application
    };

    private static List<Type> ConcreteImplementationsOf<T>() =>
        PlatformAssemblies
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(T).IsAssignableFrom(t))
            .ToList();

    [Fact]
    public void ExactlyOne_IMultiChatSessionStore_Implementation()
    {
        Assert.Single(ConcreteImplementationsOf<IMultiChatSessionStore>());
    }

    [Fact]
    public void MultiChatQuadrantRules_IsTheSingleStaticImplementation()
    {
        var type = typeof(MultiChatQuadrantRules);

        // A C# static class compiles to abstract + sealed.
        Assert.True(type is { IsAbstract: true, IsSealed: true });
    }
}
