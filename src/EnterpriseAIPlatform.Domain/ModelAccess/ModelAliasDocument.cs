namespace EnterpriseAIPlatform.Domain.ModelAccess;

/// <summary>Maps a retired model id forward to its replacement (spec 014 Key Entities).</summary>
public sealed class ModelAliasDocument
{
    /// <summary>Canonical id of the retired model. Primary key.</summary>
    public required string RetiredModelId { get; set; }

    public required string ReplacementModelId { get; set; }
}
