namespace EnterpriseAIPlatform.Domain.ModelAccess;

/// <summary>
/// The pre-filtered allow-list of models eligible for AI-assisted persona/prompt generation
/// (spec 014 Key Entities) — distinct from, and a subset of, the general model registry.
/// Read-open (FR-005); write admin-gated (FR-007).
/// </summary>
public sealed class PersonaGenerationModelConfig
{
    /// <summary>Singleton row id — always 1.</summary>
    public int Id { get; set; } = 1;

    public List<string> AllowedModelIds { get; set; } = new();

    /// <summary>
    /// The model AI-assisted generation calls first (spec 016 FR-010). Nullable: a deployment that
    /// has not chosen one yet is a configuration error surfaced at call time, not a silent fallback
    /// to an arbitrary model. Must be a member of <see cref="AllowedModelIds"/> — validated at the
    /// point of use, since the allow-list can be edited independently of this selection.
    /// </summary>
    public string? PrimaryModelId { get; set; }

    /// <summary>
    /// The model tried if <see cref="PrimaryModelId"/> fails (spec 016 FR-010). Tried at most once —
    /// there is deliberately no retry chain. Nullable: no fallback configured means a primary
    /// failure surfaces immediately as a structured error rather than being retried indefinitely.
    /// Must also be a member of <see cref="AllowedModelIds"/>.
    /// </summary>
    public string? FallbackModelId { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public string? UpdatedByUserId { get; set; }
}
