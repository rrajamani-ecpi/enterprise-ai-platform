namespace EnterpriseAIPlatform.Domain.ModelAccess;

/// <summary>Access tier for cost-routing/UI decisions (spec 014 FR-011).</summary>
public enum ModelAccessTier
{
    Standard,
    Advanced,
}

/// <summary>
/// Per-model registry entry (spec 014 Key Entities). Capability flags are nullable so a
/// missing/unconfirmed value is distinguishable from an explicit "false" — the write path
/// (<c>IModelCatalogService.UpsertAsync</c>) rejects any null flag rather than defaulting it
/// (Edge Case: no silent "supported" default).
/// </summary>
public sealed class ModelConfigDocument
{
    /// <summary>Canonical ID in <c>provider:modelId</c> form (FR-011). Primary key.</summary>
    public required string Id { get; set; }

    public required string DisplayName { get; set; }

    public required string Provider { get; set; }

    public bool IsEnabled { get; set; }

    /// <summary>Gate 3 of the FR-003 intersection — only applies when true.</summary>
    public bool RequiresAdvancedModelAccess { get; set; }

    public bool? SupportsToolCalling { get; set; }

    public bool? SupportsVision { get; set; }

    public bool? SupportsReasoning { get; set; }

    public ModelAccessTier AccessTier { get; set; }

    public int ContextWindowSize { get; set; }

    public decimal PricingInputPerMillionTokens { get; set; }

    public decimal PricingOutputPerMillionTokens { get; set; }

    /// <summary>Soft-delete flag (FR-002). The row is never physically removed.</summary>
    public bool IsDeleted { get; set; }
}
