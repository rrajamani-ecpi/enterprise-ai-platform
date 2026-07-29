using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Domain.ModelAccess;

namespace EnterpriseAIPlatform.Application.ModelAccess;

/// <summary>
/// The persona-generation model allow-list (spec 014 FR-005/007/009). <see cref="GetAsync"/> is
/// open to any authenticated caller; the endpoint layer applies <c>RequireAdmin</c> to the write
/// methods, not this service.
/// </summary>
public interface IPersonaGenerationModelConfigService
{
    /// <summary>Returns an empty allow-list if no config exists yet (Edge Case).</summary>
    Task<ServerActionResponse<PersonaGenerationModelConfig>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the allow-list. Each id must exist in the general registry and not be deleted
    /// (FR-009 corollary) — a broader, otherwise-valid model outside this check still can't be added.
    /// </summary>
    Task<ServerActionResponse<bool>> SetAllowedModelsAsync(
        IReadOnlyList<string> modelIds,
        string updatedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a persona-generation model *selection* (as opposed to an admin editing the
    /// allow-list itself). Fails closed with an actionable error if the allow-list is empty or
    /// misconfigured — it never falls back to the general registry (Edge Case).
    /// </summary>
    Task<ServerActionResponse<bool>> ValidateSelectionAsync(
        string modelId, CancellationToken cancellationToken = default);
}
