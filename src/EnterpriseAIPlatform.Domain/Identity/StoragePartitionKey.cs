namespace EnterpriseAIPlatform.Domain.Identity;

/// <summary>
/// A storage partition key derived from a hashed identity (never the raw identifier).
/// See spec 002 FR-014 / User Story 6.
/// </summary>
public readonly record struct StoragePartitionKey(string Value)
{
    public override string ToString() => Value;
}
