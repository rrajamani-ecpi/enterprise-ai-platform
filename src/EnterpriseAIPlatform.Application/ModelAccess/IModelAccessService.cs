using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Domain.ModelAccess;

namespace EnterpriseAIPlatform.Application.ModelAccess;

/// <summary>
/// Computes the caller's effective set of available chat models (spec 014 FR-003). The result is
/// computed fresh on every call from the current <see cref="UserModel"/> and current registry
/// state — never cached per-user. Exactly one implementation (architecture-tested).
/// </summary>
public interface IModelAccessService
{
    Task<ServerActionResponse<IReadOnlyList<ModelConfigDocument>>> GetAvailableModelsAsync(
        UserModel caller,
        CancellationToken cancellationToken = default);
}
