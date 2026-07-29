using System.Reflection;
using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Infrastructure.Chat;
using Xunit;

namespace EnterpriseAIPlatform.ArchitectureTests;

/// <summary>Spec 004, Constitution Principle IV: exactly one implementation per pipeline-stage interface.</summary>
public class ChatSingleImplementationTests
{
    private static readonly Assembly[] PlatformAssemblies =
    {
        typeof(ChatPipeline).Assembly, // Infrastructure
        typeof(IChatPipeline).Assembly, // Application
    };

    private static List<Type> ConcreteImplementationsOf<T>() =>
        PlatformAssemblies
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(T).IsAssignableFrom(t))
            .ToList();

    [Fact]
    public void ExactlyOne_IChatPipeline_Implementation()
    {
        Assert.Single(ConcreteImplementationsOf<IChatPipeline>());
    }

    [Fact]
    public void ExactlyOne_IPiiRedactor_Implementation()
    {
        Assert.Single(ConcreteImplementationsOf<IPiiRedactor>());
    }

    [Fact]
    public void ExactlyOne_IContentSafetyGuard_Implementation()
    {
        Assert.Single(ConcreteImplementationsOf<IContentSafetyGuard>());
    }

    [Fact]
    public void ExactlyOne_IChatCompletionClient_Implementation_InR1()
    {
        // R1 registers exactly one provider (azure-foundry). When R2 adds more, this should
        // become a per-Provider-key duplicate check instead of a single-type one (mirrors spec
        // 014's ModelAccessSingleImplementationTests.ExactlyOneAdapter_Registered_InR1).
        Assert.Single(ConcreteImplementationsOf<IChatCompletionClient>());
    }

    [Fact]
    public void ExactlyOne_IChatThreadStore_And_IChatMessageStore_Implementation()
    {
        Assert.Single(ConcreteImplementationsOf<IChatThreadStore>());
        Assert.Single(ConcreteImplementationsOf<IChatMessageStore>());
    }
}
