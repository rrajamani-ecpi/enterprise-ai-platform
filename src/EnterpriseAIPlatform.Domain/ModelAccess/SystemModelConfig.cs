namespace EnterpriseAIPlatform.Domain.ModelAccess;

/// <summary>
/// Admin singleton (spec 014 Key Entities). Read path is cached with a resilient fallback to
/// hardcoded defaults if the config store is unavailable (FR-014).
/// </summary>
public sealed class SystemModelConfig
{
    /// <summary>Singleton row id — always 1.</summary>
    public int Id { get; set; } = 1;

    /// <summary>
    /// Per-role allow-list of canonical model ids, keyed by role name (<c>admin</c>, <c>staff</c>,
    /// <c>faculty</c>, <c>student</c>, <c>default</c>). A single-element list containing
    /// <c>"*"</c> denotes "all models" for that role (a simplification of the spec's
    /// <c>string[] | "*"</c> shape into one consistently-typed list).
    /// </summary>
    public Dictionary<string, List<string>> RoleModelAccess { get; set; } = new();

    public string? EmbeddingModelId { get; set; }

    public string? ImageModelId { get; set; }

    public string? ArtifactModelId { get; set; }

    public required string FallbackModelId { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public string? UpdatedByUserId { get; set; }
}
