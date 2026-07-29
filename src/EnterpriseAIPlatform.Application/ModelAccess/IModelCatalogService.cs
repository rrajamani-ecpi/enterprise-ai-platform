using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Domain.ModelAccess;

namespace EnterpriseAIPlatform.Application.ModelAccess;

/// <summary>
/// The model registry (spec 014 FR-002, FR-011). <see cref="SoftDeleteAsync"/> MUST NOT
/// physically remove the underlying row (SC-003).
/// </summary>
public interface IModelCatalogService
{
    Task<ServerActionResponse<ModelConfigDocument>> GetAsync(
        string canonicalId, bool includeDeleted = false, CancellationToken cancellationToken = default);

    Task<ServerActionResponse<IReadOnlyList<ModelConfigDocument>>> ListAsync(
        bool includeDeleted = false, CancellationToken cancellationToken = default);

    /// <summary>Rejects a model with any capability flag unset (Edge Case — no implicit default).</summary>
    Task<ServerActionResponse<bool>> UpsertAsync(
        ModelConfigDocument model, CancellationToken cancellationToken = default);

    Task<ServerActionResponse<bool>> SoftDeleteAsync(
        string canonicalId, CancellationToken cancellationToken = default);

    /// <summary>Follows <see cref="ModelAliasDocument"/> if the id has been retired.</summary>
    Task<ServerActionResponse<string>> ResolveAliasAsync(
        string canonicalId, CancellationToken cancellationToken = default);
}
