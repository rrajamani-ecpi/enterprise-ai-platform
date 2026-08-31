using System.Reflection;
using EnterpriseAIPlatform.Application.Sharing;
using EnterpriseAIPlatform.Infrastructure.Sharing;
using Xunit;

namespace EnterpriseAIPlatform.ArchitectureTests;

/// <summary>
/// Spec 018 SC-001/T022, Constitution Principle IV: exactly one <see cref="ISharingPolicyService"/>
/// implementation, and <see cref="SharingPolicyEvaluator"/> is the single static evaluator.
/// </summary>
public class SharingSingleImplementationTests
{
    private static readonly Assembly[] PlatformAssemblies =
    {
        typeof(SharingPolicyService).Assembly, // Infrastructure
        typeof(ISharingPolicyService).Assembly, // Application
    };

    private static List<Type> ConcreteImplementationsOf<T>() =>
        PlatformAssemblies
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(T).IsAssignableFrom(t))
            .ToList();

    [Fact]
    public void ExactlyOne_ISharingPolicyService_Implementation()
    {
        Assert.Single(ConcreteImplementationsOf<ISharingPolicyService>());
    }

    [Fact]
    public void SharingPolicyEvaluator_IsTheSingleStaticImplementation()
    {
        var type = typeof(SharingPolicyEvaluator);

        // A C# static class compiles to abstract + sealed.
        Assert.True(type is { IsAbstract: true, IsSealed: true });
    }
}
