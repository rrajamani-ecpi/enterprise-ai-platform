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

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public string? UpdatedByUserId { get; set; }
}
