using EnterpriseAIPlatform.Domain.Support;

namespace EnterpriseAIPlatform.Application.Support;

/// <summary>The single implementation persists to spec 002's <c>users</c> Cosmos container (spec 017 FR-012).</summary>
public interface IVersionAcknowledgmentStore
{
    /// <summary><c>null</c> — not an error — for a caller who has never acknowledged anything.</summary>
    Task<VersionAcknowledgmentModel?> GetAsync(string ownerPartitionKey, CancellationToken cancellationToken = default);

    Task SetAsync(string ownerPartitionKey, string acknowledgedVersion, CancellationToken cancellationToken = default);
}
