using System.Security.Cryptography;
using System.Text;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Domain.Identity;

namespace EnterpriseAIPlatform.Infrastructure.Identity;

/// <summary>
/// Deterministic, non-reversible partition-key hasher (spec 002 FR-014, SC-008):
/// SHA-256 over the normalized (lowercased, trimmed) email. Two casing/whitespace
/// variants of the same email produce the identical key; the raw email is never used.
/// </summary>
public sealed class IdentityHasher : IIdentityHasher
{
    public StoragePartitionKey ForEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var normalized = email.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return new StoragePartitionKey(Convert.ToHexStringLower(hash));
    }
}
