using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.ModelAccess;

namespace EnterpriseAIPlatform.Application.Chat;

/// <summary>
/// Invokes a model and streams its response (spec 004 D4/D8/D9; extends spec 014's FR-009
/// tool-call reliability pattern to the model call itself). One implementation per provider,
/// mirroring spec 014's <see cref="IModelProviderAdapter"/> one-adapter-per-provider guarantee.
/// Reuses spec 014's <see cref="ChatRequest"/>/<see cref="ChatMessage"/> types rather than
/// redefining a parallel shape (Principle IV).
/// </summary>
public interface IChatCompletionClient
{
    string Provider { get; }

    IAsyncEnumerable<string> StreamCompletionAsync(
        ChatRequest request, ModelConfigDocument model, CancellationToken cancellationToken = default);
}
