using EnterpriseAIPlatform.Domain.ModelAccess;

namespace EnterpriseAIPlatform.Application.ModelAccess;

/// <summary>
/// Redis-backed cache in front of <see cref="ISystemModelConfigStore"/> (spec 014 FR-014/SC-010).
/// <see cref="GetAsync"/> MUST NEVER throw: cache unavailable falls through to the store; store
/// also unavailable falls through to <see cref="HardcodedDefaults.SystemModelConfig"/> — never a
/// hard failure of the dependent request (Constitution Principle III's documented fail-open carve-out).
/// </summary>
public interface ISystemModelConfigCache
{
    Task<SystemModelConfig> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Called after an admin write so the next read observes the change.</summary>
    Task InvalidateAsync(CancellationToken cancellationToken = default);
}
