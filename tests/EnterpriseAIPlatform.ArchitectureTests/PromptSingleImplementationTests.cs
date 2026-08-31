using System.Reflection;
using EnterpriseAIPlatform.Application.Prompts;
using EnterpriseAIPlatform.Application.Sharing;
using EnterpriseAIPlatform.Domain.Prompts;
using EnterpriseAIPlatform.Infrastructure.Prompts;
using Xunit;

namespace EnterpriseAIPlatform.ArchitectureTests;

/// <summary>
/// Spec 016 T064/T065, Constitution Principle IV and FR-009. Pins the two structural properties
/// that a later change could silently undo: prompt sharing is decided by spec 018's policy service
/// rather than a prompt-local copy, and no resource-gated prompt path can return
/// <c>NOT_FOUND</c> — which maps to 404 and would hand an unauthorized caller an existence oracle.
/// </summary>
public class PromptSingleImplementationTests
{
    private static readonly Assembly[] PlatformAssemblies =
    {
        typeof(PromptService).Assembly, // Infrastructure
        typeof(IPromptService).Assembly, // Application
    };

    private static List<Type> ConcreteImplementationsOf<T>() =>
        PlatformAssemblies
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(T).IsAssignableFrom(t))
            .ToList();

    [Fact]
    public void ExactlyOne_IPromptService_Implementation()
    {
        Assert.Single(ConcreteImplementationsOf<IPromptService>());
    }

    [Fact]
    public void ExactlyOne_IPromptGenerationService_Implementation()
    {
        Assert.Single(ConcreteImplementationsOf<IPromptGenerationService>());
    }

    [Fact]
    public void PromptService_DependsOnTheSharedSharingPolicyService()
    {
        var constructor = Assert.Single(typeof(PromptService).GetConstructors());

        Assert.Contains(constructor.GetParameters(), p => p.ParameterType == typeof(ISharingPolicyService));
    }

    [Fact]
    public void NoPromptLocalSharingEvaluatorExists()
    {
        // A second evaluator would be a second answer to "may this be shared?" (Principle IV).
        var promptLocalEvaluators = PlatformAssemblies
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Namespace?.Contains("Prompts", StringComparison.Ordinal) == true)
            .Where(t => t.Name.Contains("Sharing", StringComparison.Ordinal)
                     || t.Name.Contains("ShareEvaluator", StringComparison.Ordinal)
                     || t.Name.Contains("SharePolicy", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(promptLocalEvaluators);
    }

    [Fact]
    public void PromptAccessEvaluator_IsTheSingleStaticImplementation()
    {
        var type = typeof(PromptAccessEvaluator);

        // A C# static class compiles to abstract + sealed.
        Assert.True(type is { IsAbstract: true, IsSealed: true });
    }

    [Fact]
    public void NoResourceGatedPromptPath_ReturnsNotFound()
    {
        // FR-009: 404 and 401 are distinguishable by a caller, so a missing prompt and a forbidden
        // one must produce the same status. Enforced against the compiled IL so it holds for every
        // method on the service, including ones added after this test was written.
        var notFoundFactory = typeof(Application.Common.ServerActionResponse<>)
            .GetMethod("NotFound", BindingFlags.Public | BindingFlags.Static)!;

        var offenders = new List<string>();
        foreach (var method in typeof(PromptService).GetMethods(
                     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            var body = method.GetMethodBody();
            if (body is null)
            {
                continue;
            }

            if (CallsNotFoundFactory(method, notFoundFactory))
            {
                offenders.Add(method.Name);
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Scans a method's IL for a call to <c>ServerActionResponse&lt;T&gt;.NotFound</c>. Reading the
    /// bytes directly (rather than grepping source) means the assertion survives refactors,
    /// renames, and file moves.
    /// </summary>
    private static bool CallsNotFoundFactory(MethodBase method, MethodInfo notFoundFactory)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            return false;
        }

        var module = method.Module;
        for (var i = 0; i + 4 < il.Length; i++)
        {
            // 0x28 = call, 0x6F = callvirt. Both are followed by a 4-byte metadata token.
            if (il[i] != 0x28 && il[i] != 0x6F)
            {
                continue;
            }

            var token = BitConverter.ToInt32(il, i + 1);
            MethodBase? resolved;
            try
            {
                resolved = module.ResolveMethod(
                    token,
                    method.DeclaringType?.GetGenericArguments(),
                    method.IsGenericMethod ? method.GetGenericArguments() : null);
            }
            catch (Exception)
            {
                // Not every byte sequence that looks like an opcode is one; an unresolvable token
                // simply isn't the call we're looking for.
                continue;
            }

            if (resolved is not null
                && resolved.Name == notFoundFactory.Name
                && resolved.DeclaringType?.IsGenericType == true
                && resolved.DeclaringType.GetGenericTypeDefinition() == notFoundFactory.DeclaringType)
            {
                return true;
            }
        }

        return false;
    }
}
