using EnterpriseAIPlatform.Domain.Identity;

namespace EnterpriseAIPlatform.Application.Identity;

/// <summary>
/// Produces a deterministic, non-reversible storage partition key from a user identity
/// (spec 002 FR-014). One implementation only (Constitution Principle IV).
/// </summary>
public interface IIdentityHasher
{
    StoragePartitionKey ForEmail(string email);
}
