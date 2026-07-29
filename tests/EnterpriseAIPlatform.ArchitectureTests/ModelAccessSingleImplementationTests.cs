using System.Reflection;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Infrastructure.ModelAccess;
using EnterpriseAIPlatform.Infrastructure.ModelProviders;
using Xunit;

namespace EnterpriseAIPlatform.ArchitectureTests;

/// <summary>
/// Spec 014 SC-002/T017/T041, Constitution Principle IV: exactly one effective-access computation
/// and, per registered provider, exactly one <see cref="IModelProviderAdapter"/> implementation.
/// </summary>
public class ModelAccessSingleImplementationTests
{
    private static readonly Assembly[] PlatformAssemblies =
    {
        typeof(ModelAccessService).Assembly,       // Infrastructure
        typeof(IModelAccessService).Assembly,       // Application
    };

    private static List<Type> ConcreteImplementationsOf<T>() =>
        PlatformAssemblies
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(T).IsAssignableFrom(t))
            .ToList();

    [Fact]
    public void ExactlyOne_IModelAccessService_Implementation()
    {
        Assert.Single(ConcreteImplementationsOf<IModelAccessService>());
    }

    [Fact]
    public void ModelAccessEvaluator_IsTheSingleStaticImplementation()
    {
        var type = typeof(ModelAccessEvaluator);

        // A C# static class compiles to abstract + sealed.
        Assert.True(type is { IsAbstract: true, IsSealed: true });
    }

    [Fact]
    public void ExactlyOneAdapter_Registered_InR1()
    {
        // R1 registers exactly one IModelProviderAdapter (AzureFoundryProviderAdapter). When R2
        // adds more, this should become a per-Provider-key duplicate check instead of a single-type one.
        var adapters = ConcreteImplementationsOf<IModelProviderAdapter>();

        Assert.Single(adapters);
        Assert.Equal(typeof(AzureFoundryProviderAdapter), adapters[0]);
    }
}
