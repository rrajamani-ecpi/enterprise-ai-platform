namespace EnterpriseAIPlatform.Domain.Chat;

/// <summary>
/// A user's persisted multi-chat layout (spec 006 Key Entities) — quadrant count and, per
/// quadrant, the assigned model/persona and associated thread. One session per user
/// (spec.md Assumptions). <see cref="Quadrants"/> is always 2–4 long; enforced by
/// <c>IMultiChatSessionStore</c>, never by this type alone.
/// </summary>
public sealed class MultiChatSession
{
    public required string Id { get; set; }

    /// <summary>Spec 002 <c>StoragePartitionKey</c> value — the Cosmos partition key.</summary>
    public required string PartitionKey { get; set; }

    public required string OwnerUserId { get; set; }

    public List<MultiChatQuadrant> Quadrants { get; set; } = new();

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>
/// One quadrant of a <see cref="MultiChatSession"/>. <see cref="PersonaId"/> is always null in
/// R1 (personas — specs 009/010 — aren't built yet); only <see cref="ModelId"/> assignment is
/// functional (spec 006 plan.md Summary).
/// </summary>
public sealed class MultiChatQuadrant
{
    public required int Position { get; set; }

    public string? PersonaId { get; set; }

    public string? ModelId { get; set; }

    public string? ThreadId { get; set; }
}
