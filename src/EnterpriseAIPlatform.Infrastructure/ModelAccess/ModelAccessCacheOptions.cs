namespace EnterpriseAIPlatform.Infrastructure.ModelAccess;

/// <summary>Azure Cache for Redis connection settings fronting <see cref="Domain.ModelAccess.SystemModelConfig"/> reads (FR-014).</summary>
public sealed class ModelAccessCacheOptions
{
    public const string SectionName = "ModelAccessCache";

    public string? RedisConnectionString { get; init; }
}
