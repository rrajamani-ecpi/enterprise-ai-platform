using System.Text.Json;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.ModelAccess;
using EnterpriseAIPlatform.Infrastructure.ModelProviders;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseAIPlatform.Infrastructure.ModelAccess;

/// <summary>
/// EF Core context for the strongly relational, schema-stable admin/system config entities
/// (spec 014 data-model.md) — Azure SQL per the constitution's Data &amp; Storage guidance, distinct
/// from spec 002's Cosmos usage for session-scoped documents.
/// </summary>
public sealed class ModelAccessDbContext : DbContext
{
    public ModelAccessDbContext(DbContextOptions<ModelAccessDbContext> options) : base(options)
    {
    }

    public DbSet<ModelConfigDocument> ModelConfigs => Set<ModelConfigDocument>();

    public DbSet<SystemModelConfig> SystemModelConfigs => Set<SystemModelConfig>();

    public DbSet<ModelAliasDocument> ModelAliases => Set<ModelAliasDocument>();

    public DbSet<MessageLimitConfig> MessageLimitConfigs => Set<MessageLimitConfig>();

    public DbSet<PersonaGenerationModelConfig> PersonaGenerationModelConfigs => Set<PersonaGenerationModelConfig>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var stringListConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<string>, string>(
            v => JsonSerializer.Serialize(v, JsonOptions),
            v => JsonSerializer.Deserialize<List<string>>(v, JsonOptions) ?? new List<string>());

        var stringListComparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
            (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
            v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
            v => v.ToList());

        var roleModelAccessConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<Dictionary<string, List<string>>, string>(
            v => JsonSerializer.Serialize(v, JsonOptions),
            v => JsonSerializer.Deserialize<Dictionary<string, List<string>>>(v, JsonOptions) ?? new Dictionary<string, List<string>>());

        var roleModelAccessComparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<Dictionary<string, List<string>>>(
            (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
            v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
            v => new Dictionary<string, List<string>>(v));

        modelBuilder.Entity<ModelConfigDocument>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.HasQueryFilter(m => !m.IsDeleted);

            // T037 (US6): the one R1 Azure/Foundry model, so the catalog/access endpoints have a
            // real entry to serve without requiring an admin write first.
            entity.HasData(new ModelConfigDocument
            {
                Id = HardcodedDefaults.AzureFoundryDefaultModelId,
                DisplayName = "GPT-5 (Azure Foundry)",
                Provider = AzureFoundryProviderAdapter.ProviderKey,
                IsEnabled = true,
                RequiresAdvancedModelAccess = false,
                SupportsToolCalling = true,
                SupportsVision = true,
                SupportsReasoning = true,
                AccessTier = ModelAccessTier.Standard,
                ContextWindowSize = 272_000,
                PricingInputPerMillionTokens = 0m,
                PricingOutputPerMillionTokens = 0m,
                IsDeleted = false,
            });
        });

        modelBuilder.Entity<SystemModelConfig>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.RoleModelAccess)
                .HasConversion(roleModelAccessConverter)
                .Metadata.SetValueComparer(roleModelAccessComparer);
        });

        modelBuilder.Entity<ModelAliasDocument>(entity => entity.HasKey(a => a.RetiredModelId));

        modelBuilder.Entity<MessageLimitConfig>(entity => entity.HasKey(c => c.Id));

        modelBuilder.Entity<PersonaGenerationModelConfig>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.AllowedModelIds)
                .HasConversion(stringListConverter)
                .Metadata.SetValueComparer(stringListComparer);
        });
    }

    private static readonly JsonSerializerOptions JsonOptions = new();
}
