using System.Reflection;
using EnterpriseAIPlatform.Application.Support;
using EnterpriseAIPlatform.Infrastructure.Support;
using Xunit;

namespace EnterpriseAIPlatform.ArchitectureTests;

/// <summary>Spec 017, Constitution Principle IV: exactly one implementation per support-feature contract.</summary>
public class SupportSingleImplementationTests
{
    private static readonly Assembly[] PlatformAssemblies =
    {
        typeof(FileSystemChangelogReader).Assembly, // Infrastructure
        typeof(IChangelogReader).Assembly, // Application
    };

    private static List<Type> ConcreteImplementationsOf<T>() =>
        PlatformAssemblies
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(T).IsAssignableFrom(t))
            .ToList();

    [Fact]
    public void ExactlyOne_IChangelogReader_Implementation()
    {
        Assert.Single(ConcreteImplementationsOf<IChangelogReader>());
    }

    [Fact]
    public void ExactlyOne_IVersionAcknowledgmentStore_Implementation()
    {
        Assert.Single(ConcreteImplementationsOf<IVersionAcknowledgmentStore>());
    }

    [Fact]
    public void ExactlyOne_IFeedbackForwarder_Implementation()
    {
        Assert.Single(ConcreteImplementationsOf<IFeedbackForwarder>());
    }

    [Fact]
    public void AlertWindowEvaluator_IsTheSingleStaticImplementation()
    {
        var type = typeof(AlertWindowEvaluator);

        // A C# static class compiles to abstract + sealed.
        Assert.True(type is { IsAbstract: true, IsSealed: true });
    }
}
