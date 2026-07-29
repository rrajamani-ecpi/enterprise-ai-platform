using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.ModelAccess;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseAIPlatform.Infrastructure.ModelAccess;

/// <summary>
/// The single implementation of the persona-generation-model allow-list (spec 014 FR-005/007/009).
/// </summary>
public sealed class PersonaGenerationModelConfigService : IPersonaGenerationModelConfigService
{
    private readonly ModelAccessDbContext _db;

    public PersonaGenerationModelConfigService(ModelAccessDbContext db) => _db = db;

    public async Task<ServerActionResponse<PersonaGenerationModelConfig>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.PersonaGenerationModelConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == 1, cancellationToken);

        // Fresh-tenant default: empty allow-list, never an error (Edge Case).
        return ServerActionResponse<PersonaGenerationModelConfig>.Ok(existing ?? new PersonaGenerationModelConfig());
    }

    public async Task<ServerActionResponse<bool>> SetAllowedModelsAsync(
        IReadOnlyList<string> modelIds, string updatedByUserId, CancellationToken cancellationToken = default)
    {
        // Every id must exist in the general registry and not be soft-deleted (FR-009 corollary) —
        // an admin can't curate the allow-list with a model that doesn't (or no longer) exist.
        var validIds = await _db.ModelConfigs.AsNoTracking()
            .Where(m => modelIds.Contains(m.Id))
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        var invalid = modelIds.Except(validIds).ToList();
        if (invalid.Count > 0)
        {
            return ServerActionResponse<bool>.Error(
                $"The following model ids are not in the general registry (or are deleted): {string.Join(", ", invalid)}.");
        }

        var existing = await _db.PersonaGenerationModelConfigs.FirstOrDefaultAsync(c => c.Id == 1, cancellationToken);
        if (existing is null)
        {
            existing = new PersonaGenerationModelConfig();
            _db.PersonaGenerationModelConfigs.Add(existing);
        }

        existing.AllowedModelIds = modelIds.ToList();
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        existing.UpdatedByUserId = updatedByUserId;

        await _db.SaveChangesAsync(cancellationToken);
        return ServerActionResponse<bool>.Ok(true);
    }

    public async Task<ServerActionResponse<bool>> ValidateSelectionAsync(
        string modelId, CancellationToken cancellationToken = default)
    {
        var config = await _db.PersonaGenerationModelConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == 1, cancellationToken);

        if (config is null || config.AllowedModelIds.Count == 0)
        {
            // Fail closed with an actionable error — never fall back to the general registry (Edge Case).
            return ServerActionResponse<bool>.Error(
                "The persona-generation model allow-list is empty or not yet configured; no model may be selected.");
        }

        return config.AllowedModelIds.Contains(modelId)
            ? ServerActionResponse<bool>.Ok(true)
            : ServerActionResponse<bool>.Error($"Model '{modelId}' is not on the persona-generation allow-list.");
    }
}
