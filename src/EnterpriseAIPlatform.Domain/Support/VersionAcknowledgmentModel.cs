namespace EnterpriseAIPlatform.Domain.Support;

/// <summary>
/// Per-user persisted acknowledgment of a changelog version (spec 017 Key Entities, FR-012). A
/// singleton row per user, in spec 002's <c>users</c> Cosmos container.
/// </summary>
public sealed class VersionAcknowledgmentModel
{
    public required string Id { get; set; }

    /// <summary>Spec 002 <c>StoragePartitionKey</c> value.</summary>
    public required string PartitionKey { get; set; }

    public required string AcknowledgedVersion { get; set; }

    /// <summary>The timestamp the 60-day cooldown (FR-011) is measured from.</summary>
    public DateTimeOffset AcknowledgedAtUtc { get; set; }
}
