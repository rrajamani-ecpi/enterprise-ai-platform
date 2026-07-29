using EnterpriseAIPlatform.Domain.ModelAccess;

namespace EnterpriseAIPlatform.Application.ModelAccess;

/// <summary>The durable (SQL) read/write path for <see cref="SystemModelConfig"/>, behind <see cref="ISystemModelConfigCache"/>.</summary>
public interface ISystemModelConfigStore
{
    Task<SystemModelConfig> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(SystemModelConfig config, CancellationToken cancellationToken = default);
}
