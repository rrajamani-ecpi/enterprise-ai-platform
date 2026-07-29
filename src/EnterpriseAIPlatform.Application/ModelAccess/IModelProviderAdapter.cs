using EnterpriseAIPlatform.Domain.ModelAccess;

namespace EnterpriseAIPlatform.Application.ModelAccess;

/// <summary>A single chat message in the provider-agnostic shape the chat interface (spec 004) sends.</summary>
public sealed record ChatMessage(string Role, string Content);

/// <summary>The provider-agnostic chat request the client-facing interface produces (spec 014 FR-012).</summary>
public sealed record ChatRequest(IReadOnlyList<ChatMessage> Messages);

/// <summary>The provider-agnostic chat response the client-facing interface expects back (FR-012).</summary>
public sealed record ChatResponse(string Content, bool IsError, string? ErrorMessage = null);

/// <summary>
/// A request already adapted into a specific provider's expected shape. <see cref="Headers"/> is
/// kept separate from <see cref="Payload"/> so an auth token never gets serialized as part of the
/// request body by accident.
/// </summary>
public sealed record ProviderRequest(
    string Provider,
    IReadOnlyDictionary<string, object?> Payload,
    IReadOnlyDictionary<string, string>? Headers = null);

/// <summary>A raw response as returned by a specific provider, before normalization.</summary>
public sealed record ProviderResponse(string Provider, IReadOnlyDictionary<string, object?> Payload, bool IsError = false);

/// <summary>
/// Adapts requests/responses per provider behind one chat interface (spec 014 FR-012, US7/US8).
/// One implementation per distinct <see cref="Provider"/> value (architecture-tested) — R1
/// registers only <c>AzureFoundryProviderAdapter</c>; R2 adds the remaining providers against this
/// same interface without touching any R1 caller.
/// </summary>
public interface IModelProviderAdapter
{
    /// <summary>The provider key this adapter serves, e.g. <c>"azure-foundry"</c>.</summary>
    string Provider { get; }

    /// <summary>Translates message-role conventions and strips provider-only fields (FR-012).</summary>
    Task<ProviderRequest> AdaptRequestAsync(
        ChatRequest request, ModelConfigDocument model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Normalizes a provider response into the common shape. An unexpected/error provider shape
    /// MUST surface as a consistent <see cref="ChatResponse"/> error, never leak the provider's
    /// raw shape to the caller (Edge Cases).
    /// </summary>
    Task<ChatResponse> AdaptResponseAsync(
        ProviderResponse response, ModelConfigDocument model, CancellationToken cancellationToken = default);
}
