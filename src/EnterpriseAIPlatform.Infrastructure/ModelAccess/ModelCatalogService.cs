using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.ModelAccess;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseAIPlatform.Infrastructure.ModelAccess;

/// <summary>
/// The single implementation of the model registry (spec 014 FR-002/FR-011). Deletion is always
/// soft (SC-003); writes reject any model with an unset capability flag (Edge Case).
/// </summary>
public sealed class ModelCatalogService : IModelCatalogService
{
    private readonly ModelAccessDbContext _db;

    public ModelCatalogService(ModelAccessDbContext db) => _db = db;

    public async Task<ServerActionResponse<ModelConfigDocument>> GetAsync(
        string canonicalId, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var query = includeDeleted ? _db.ModelConfigs.IgnoreQueryFilters() : _db.ModelConfigs;
        var model = await query.AsNoTracking().FirstOrDefaultAsync(m => m.Id == canonicalId, cancellationToken);

        return model is null
            ? ServerActionResponse<ModelConfigDocument>.NotFound($"Model '{canonicalId}' was not found.")
            : ServerActionResponse<ModelConfigDocument>.Ok(model);
    }

    public async Task<ServerActionResponse<IReadOnlyList<ModelConfigDocument>>> ListAsync(
        bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var query = includeDeleted ? _db.ModelConfigs.IgnoreQueryFilters() : _db.ModelConfigs;
        var models = await query.AsNoTracking().ToListAsync(cancellationToken);

        return ServerActionResponse<IReadOnlyList<ModelConfigDocument>>.Ok(models);
    }

    public async Task<ServerActionResponse<bool>> UpsertAsync(
        ModelConfigDocument model, CancellationToken cancellationToken = default)
    {
        if (model.SupportsToolCalling is null || model.SupportsVision is null || model.SupportsReasoning is null)
        {
            return ServerActionResponse<bool>.Error(
                "Every capability flag (tool calling, vision, reasoning) must be explicitly set; none may be left unconfigured.");
        }

        var existing = await _db.ModelConfigs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == model.Id, cancellationToken);

        if (existing is null)
        {
            _db.ModelConfigs.Add(model);
        }
        else
        {
            _db.Entry(existing).CurrentValues.SetValues(model);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ServerActionResponse<bool>.Ok(true);
    }

    public async Task<ServerActionResponse<bool>> SoftDeleteAsync(
        string canonicalId, CancellationToken cancellationToken = default)
    {
        var existing = await _db.ModelConfigs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == canonicalId, cancellationToken);

        if (existing is null)
        {
            return ServerActionResponse<bool>.NotFound($"Model '{canonicalId}' was not found.");
        }

        // Soft delete only (FR-002) — the row is updated, never removed.
        existing.IsDeleted = true;
        await _db.SaveChangesAsync(cancellationToken);
        return ServerActionResponse<bool>.Ok(true);
    }

    public async Task<ServerActionResponse<string>> ResolveAliasAsync(
        string canonicalId, CancellationToken cancellationToken = default)
    {
        var alias = await _db.ModelAliases.AsNoTracking()
            .FirstOrDefaultAsync(a => a.RetiredModelId == canonicalId, cancellationToken);

        return ServerActionResponse<string>.Ok(alias?.ReplacementModelId ?? canonicalId);
    }
}
