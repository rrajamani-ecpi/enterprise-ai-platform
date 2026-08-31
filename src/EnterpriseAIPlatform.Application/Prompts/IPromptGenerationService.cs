using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Domain.Prompts;

namespace EnterpriseAIPlatform.Application.Prompts;

/// <summary>
/// AI-assisted prompt generation (spec 016 FR-010/FR-011, contracts/service-interfaces.md).
/// Separate from <see cref="IPromptService"/> because generation neither reads nor writes the
/// prompt store — it is a model call, and folding it in would give the CRUD service a model
/// dependency it has no other use for.
/// </summary>
public interface IPromptGenerationService
{
    /// <summary>
    /// Wraps <paramref name="request"/> in the fixed prompt-engineering meta-prompt and calls the
    /// configured <c>PrimaryModelId</c>, falling back to <c>FallbackModelId</c> <b>at most once</b>
    /// on primary failure. Both ids must be members of <c>AllowedModelIds</c>; an out-of-set id is
    /// a configuration error, never a silent pass-through.
    /// <para>
    /// Returns <see cref="ResponseStatus.ERROR"/> with a populated <c>Errors</c> collection when
    /// generation cannot succeed. It never returns a success carrying an error message as its
    /// generated text (Principle III) — that is precisely the defect FR-011 exists to close.
    /// </para>
    /// </summary>
    Task<ServerActionResponse<PromptGenerationResult>> GenerateAsync(
        PromptGenerationRequest request,
        UserModel caller,
        CancellationToken cancellationToken = default);
}
