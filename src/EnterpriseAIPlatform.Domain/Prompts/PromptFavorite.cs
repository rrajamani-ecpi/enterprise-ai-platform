namespace EnterpriseAIPlatform.Domain.Prompts;

/// <summary>
/// A single user's favorite of a single prompt (spec 016 FR-016). One row per (user, prompt) rather
/// than a per-user array, so double-favoriting is idempotent by primary key and one user's favorites
/// are structurally incapable of appearing in another's list (SC-007).
/// </summary>
/// <remarks>
/// Holds a foreign key to <see cref="PromptModel"/> with <c>OnDelete(DeleteBehavior.Cascade)</c>, so
/// FR-017's "no dangling references" is a property of the schema rather than of every delete call
/// site (Constitution Principle V; research.md D7). Ownership transfer deliberately never touches
/// these rows (FR-019).
/// </remarks>
public sealed class PromptFavorite
{
    /// <summary>Composite key part 1 — the favoriting user's hashed identity.</summary>
    public required string UserPartitionKey { get; set; }

    /// <summary>Composite key part 2 — FK to <see cref="PromptModel.Id"/>, cascade delete.</summary>
    public required string PromptId { get; set; }

    public DateTimeOffset FavoritedAtUtc { get; set; }
}
