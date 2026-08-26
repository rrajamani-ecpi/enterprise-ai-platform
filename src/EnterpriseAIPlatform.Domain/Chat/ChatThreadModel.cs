namespace EnterpriseAIPlatform.Domain.Chat;

/// <summary>
/// Chat thread metadata (spec 004 Key Entities). R1 creates only <see cref="Version"/> "v3"
/// threads — the field exists so the read-only-legacy-thread gate (FR-002) has a real condition,
/// not so this build produces v1/v2 threads itself.
/// </summary>
public sealed class ChatThreadModel
{
    public required string Id { get; set; }

    /// <summary>Spec 002 <c>StoragePartitionKey</c> value — the Cosmos partition key.</summary>
    public required string PartitionKey { get; set; }

    public required string OwnerUserId { get; set; }

    public string Version { get; set; } = "v3";

    /// <summary>Canonical <c>provider:modelId</c> (spec 014), resolved through the access gate.</summary>
    public required string ModelId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Spec 024 US3 FR-007 — set to a creation-timestamp-based default at creation, renamable thereafter. Never empty/whitespace (enforced by <c>ConversationRenameRules</c>).</summary>
    public required string DisplayName { get; set; }

    /// <summary>Spec 024 US3 FR-005 — initialized to <see cref="CreatedAtUtc"/>, bumped on every message sent/received; drives the conversation list's most-recently-active-first order.</summary>
    public DateTimeOffset LastActivityAtUtc { get; set; }

    /// <summary>Server-stored; always empty in R1 (spec 019/data products not yet built) — FR-006's override defends this real, if currently-empty, field.</summary>
    public List<string> DataProducts { get; set; } = new();

    /// <summary>
    /// Back-reference to the owning <c>MultiChatSession</c> (spec 006), null for an ordinary
    /// single-chat thread. Spec 006 is the first to populate this and <see cref="MultiChatPosition"/>.
    /// </summary>
    public string? MultiChatSessionId { get; set; }

    /// <summary>The quadrant position within the session this thread belongs to (spec 006). Null for an ordinary thread.</summary>
    public int? MultiChatPosition { get; set; }
}
