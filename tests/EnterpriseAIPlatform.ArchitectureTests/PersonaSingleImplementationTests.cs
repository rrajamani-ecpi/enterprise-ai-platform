using System.Reflection;
using EnterpriseAIPlatform.Application.Personas;
using EnterpriseAIPlatform.Domain.Personas;
using EnterpriseAIPlatform.Infrastructure.Personas;
using Xunit;

namespace EnterpriseAIPlatform.ArchitectureTests;

/// <summary>
/// Spec 009 SC-006/T015/T035, Constitution Principle IV: exactly one <see cref="IPersonaService"/>
/// implementation; <see cref="PersonaPublicDTO"/> structurally has no <c>ApiKey</c> property; the
/// public <see cref="IPersonaService"/> has no <c>GetRawAsync</c> member (that lives only on the
/// internal <c>IPersonaRawAccessor</c>, compiler-restricted from <c>EnterpriseAIPlatform.Web</c>).
/// </summary>
public class PersonaSingleImplementationTests
{
    private static readonly Assembly[] PlatformAssemblies =
    {
        typeof(PersonaService).Assembly, // Infrastructure
        typeof(IPersonaService).Assembly, // Application
    };

    private static List<Type> ConcreteImplementationsOf<T>() =>
        PlatformAssemblies
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(T).IsAssignableFrom(t))
            .ToList();

    [Fact]
    public void ExactlyOne_IPersonaService_Implementation()
    {
        Assert.Single(ConcreteImplementationsOf<IPersonaService>());
    }

    [Fact]
    public void PersonaPublicDTO_HasNoApiKeyProperty()
    {
        var apiKeyProperty = typeof(PersonaPublicDTO).GetProperty("ApiKey");

        Assert.Null(apiKeyProperty);
    }

    [Fact]
    public void IPersonaService_HasNoGetRawAsyncMember()
    {
        var getRawAsync = typeof(IPersonaService).GetMethod("GetRawAsync");

        Assert.Null(getRawAsync);
    }

    [Fact]
    public void PersonaAccessEvaluator_IsTheSingleStaticImplementation()
    {
        var type = typeof(PersonaAccessEvaluator);

        // A C# static class compiles to abstract + sealed.
        Assert.True(type is { IsAbstract: true, IsSealed: true });
    }
}
