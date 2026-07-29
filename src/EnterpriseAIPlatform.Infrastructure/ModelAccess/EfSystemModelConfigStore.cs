using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.ModelAccess;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseAIPlatform.Infrastructure.ModelAccess;

/// <summary>The durable SQL read/write path for <see cref="SystemModelConfig"/>, behind <see cref="RedisSystemModelConfigCache"/>.</summary>
public sealed class EfSystemModelConfigStore : ISystemModelConfigStore
{
    private readonly ModelAccessDbContext _db;

    public EfSystemModelConfigStore(ModelAccessDbContext db) => _db = db;

    public async Task<SystemModelConfig> GetAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _db.SystemModelConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == 1, cancellationToken);

        return existing ?? HardcodedDefaults.SystemModelConfig;
    }

    public async Task SaveAsync(SystemModelConfig config, CancellationToken cancellationToken = default)
    {
        config.Id = 1;

        var existing = await _db.SystemModelConfigs.FirstOrDefaultAsync(c => c.Id == 1, cancellationToken);
        if (existing is null)
        {
            _db.SystemModelConfigs.Add(config);
        }
        else
        {
            _db.Entry(existing).CurrentValues.SetValues(config);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
