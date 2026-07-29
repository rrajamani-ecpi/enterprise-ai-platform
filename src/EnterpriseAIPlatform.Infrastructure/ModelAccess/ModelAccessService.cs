using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.ModelAccess;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseAIPlatform.Infrastructure.ModelAccess;

/// <summary>
/// The single implementation of <see cref="IModelAccessService"/> (spec 014 FR-003, SC-002).
/// Recomputes the intersection fresh on every call — never a cached per-user result.
/// </summary>
public sealed class ModelAccessService : IModelAccessService
{
    private readonly ModelAccessDbContext _db;
    private readonly ISystemModelConfigCache _systemConfigCache;

    public ModelAccessService(ModelAccessDbContext db, ISystemModelConfigCache systemConfigCache)
    {
        _db = db;
        _systemConfigCache = systemConfigCache;
    }

    public async Task<ServerActionResponse<IReadOnlyList<ModelConfigDocument>>> GetAvailableModelsAsync(
        UserModel caller, CancellationToken cancellationToken = default)
    {
        var systemConfig = await _systemConfigCache.GetAsync(cancellationToken);
        var enabledModels = await _db.ModelConfigs.AsNoTracking()
            .Where(m => m.IsEnabled)
            .ToListAsync(cancellationToken);

        var available = enabledModels
            .Where(m => ModelAccessEvaluator.ComputeEffectiveAccess(m, caller, systemConfig))
            .ToList();

        return ServerActionResponse<IReadOnlyList<ModelConfigDocument>>.Ok(available);
    }
}
