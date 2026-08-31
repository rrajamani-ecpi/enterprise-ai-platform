using System.Text;
using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Application.Prompts;
using EnterpriseAIPlatform.Domain.ModelAccess;
using EnterpriseAIPlatform.Domain.Prompts;

namespace EnterpriseAIPlatform.Infrastructure.Prompts;

/// <summary>
/// Spec 016 US3. Resolves the ordered model selection from spec 014's
/// <see cref="PersonaGenerationModelConfig"/> singleton, invokes the primary, and falls back
/// <b>exactly once</b>. Every failure path returns a structured <see cref="ServerActionResponse{T}"/>
/// so the endpoint can render JSON — the legacy defect was a plain-text 500 (FR-011).
/// </summary>
public sealed class PromptGenerationService : IPromptGenerationService
{
    /// <summary>
    /// The fixed prompt-engineering meta-prompt (FR-010). A constant, not configuration: making it
    /// editable would let a caller change the generation contract, and no requirement asks for that.
    /// </summary>
    public const string MetaPrompt =
        "You are a prompt engineer. Rewrite the user's rough intent below into a single, clear, " +
        "reusable prompt. Return only the rewritten prompt text, with no preamble, no commentary, " +
        "and no surrounding quotes.\n\nUser intent:\n";

    public const string EmptyIntentMessage = "Describe what the prompt should do before generating.";
    public const string NoPrimaryConfiguredMessage =
        "Prompt generation is not configured: no primary generation model has been selected.";
    public const string GenerationFailedMessage =
        "Prompt generation failed. The configured models could not produce a prompt. Try again, or write the prompt manually.";

    private readonly IPersonaGenerationModelConfigService _generationConfig;
    private readonly IModelCatalogService _modelCatalog;
    private readonly IEnumerable<IChatCompletionClient> _completionClients;

    public PromptGenerationService(
        IPersonaGenerationModelConfigService generationConfig,
        IModelCatalogService modelCatalog,
        IEnumerable<IChatCompletionClient> completionClients)
    {
        _generationConfig = generationConfig;
        _modelCatalog = modelCatalog;
        _completionClients = completionClients;
    }

    public async Task<ServerActionResponse<PromptGenerationResult>> GenerateAsync(
        PromptGenerationRequest request,
        UserModel caller,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Intent))
        {
            return ServerActionResponse<PromptGenerationResult>.Error(EmptyIntentMessage);
        }

        var configResult = await _generationConfig.GetAsync(cancellationToken);
        if (configResult.Status != ResponseStatus.OK || configResult.Response is null)
        {
            return ServerActionResponse<PromptGenerationResult>.Error(NoPrimaryConfiguredMessage);
        }

        var config = configResult.Response;
        if (string.IsNullOrWhiteSpace(config.PrimaryModelId))
        {
            return ServerActionResponse<PromptGenerationResult>.Error(NoPrimaryConfiguredMessage);
        }

        // FR-010: the allow-list is the validity set. A selection outside it is a configuration
        // error surfaced to the caller, never a model call made anyway.
        if (!config.AllowedModelIds.Contains(config.PrimaryModelId))
        {
            return ServerActionResponse<PromptGenerationResult>.Error(
                OutOfSetMessage(config.PrimaryModelId, "primary"));
        }

        if (config.FallbackModelId is not null && !config.AllowedModelIds.Contains(config.FallbackModelId))
        {
            return ServerActionResponse<PromptGenerationResult>.Error(
                OutOfSetMessage(config.FallbackModelId, "fallback"));
        }

        var chatRequest = new ChatRequest(new[] { new ChatMessage("user", MetaPrompt + request.Intent) });

        var primary = await TryGenerateAsync(config.PrimaryModelId, chatRequest, cancellationToken);
        if (primary is not null)
        {
            return ServerActionResponse<PromptGenerationResult>.Ok(
                new PromptGenerationResult(primary, config.PrimaryModelId, UsedFallback: false));
        }

        // Exactly one fallback attempt, and only if one is configured. No retry loop.
        if (string.IsNullOrWhiteSpace(config.FallbackModelId))
        {
            return ServerActionResponse<PromptGenerationResult>.Error(GenerationFailedMessage);
        }

        var fallback = await TryGenerateAsync(config.FallbackModelId, chatRequest, cancellationToken);
        return fallback is not null
            ? ServerActionResponse<PromptGenerationResult>.Ok(
                new PromptGenerationResult(fallback, config.FallbackModelId, UsedFallback: true))
            : ServerActionResponse<PromptGenerationResult>.Error(GenerationFailedMessage);
    }

    public static string OutOfSetMessage(string modelId, string role) =>
        $"Prompt generation is misconfigured: the {role} model '{modelId}' is not in the allowed generation model list.";

    /// <summary>
    /// One model attempt. Returns the accumulated text, or <c>null</c> if the attempt failed for any
    /// reason — including producing nothing, which is a failure rather than an empty success
    /// (Principle III). Consumes <see cref="IChatCompletionClient.StreamCompletionAsync"/> to
    /// completion instead of adding a non-streaming method to that interface (Principle IV).
    /// </summary>
    private async Task<string?> TryGenerateAsync(
        string modelId, ChatRequest chatRequest, CancellationToken cancellationToken)
    {
        try
        {
            var modelResult = await _modelCatalog.GetAsync(modelId, includeDeleted: false, cancellationToken);
            if (modelResult.Status != ResponseStatus.OK || modelResult.Response is null)
            {
                return null;
            }

            var model = modelResult.Response;
            var client = _completionClients.FirstOrDefault(c => c.Provider == model.Provider);
            if (client is null)
            {
                return null;
            }

            var builder = new StringBuilder();
            await foreach (var chunk in client.StreamCompletionAsync(chatRequest, model, cancellationToken))
            {
                builder.Append(chunk);
            }

            var text = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller-initiated cancellation is not a model failure; it must not consume the single
            // fallback attempt, so it propagates rather than being swallowed as a failed try.
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
