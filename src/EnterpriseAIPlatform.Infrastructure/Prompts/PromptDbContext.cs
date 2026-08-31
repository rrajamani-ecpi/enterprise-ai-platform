using System.Text.Json;
using EnterpriseAIPlatform.Domain.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EnterpriseAIPlatform.Infrastructure.Prompts;

/// <summary>
/// EF Core context for <see cref="PromptModel"/> and <see cref="PromptFavorite"/> (spec 016
/// data-model.md) — Azure SQL per the constitution's Data &amp; Storage guidance, which names
/// prompts explicitly, following <c>PersonaDbContext</c>'s exact pattern (JSON-column list fields,
/// own <c>Migrations</c> folder).
/// </summary>
public sealed class PromptDbContext : DbContext
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public PromptDbContext(DbContextOptions<PromptDbContext> options) : base(options)
    {
    }

    public DbSet<PromptModel> Prompts => Set<PromptModel>();

    public DbSet<PromptFavorite> PromptFavorites => Set<PromptFavorite>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var stringListConverter = new ValueConverter<List<string>, string>(
            v => JsonSerializer.Serialize(v, JsonOptions),
            v => JsonSerializer.Deserialize<List<string>>(v, JsonOptions) ?? new List<string>());

        // Without a comparer, EF Core cannot detect in-place mutation of the collection and silently
        // drops updates to it.
        var stringListComparer = new ValueComparer<List<string>>(
            (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
            v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
            v => v.ToList());

        var shareTargetListConverter = new ValueConverter<List<PromptShareTarget>, string>(
            v => JsonSerializer.Serialize(v, JsonOptions),
            v => JsonSerializer.Deserialize<List<PromptShareTarget>>(v, JsonOptions) ?? new List<PromptShareTarget>());

        var shareTargetListComparer = new ValueComparer<List<PromptShareTarget>>(
            (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
            v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
            v => v.ToList());

        modelBuilder.Entity<PromptModel>(entity =>
        {
            entity.HasKey(p => p.Id);

            entity.Property(p => p.CollaboratorPartitionKeys).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
            entity.Property(p => p.SharedWith).HasConversion(shareTargetListConverter).Metadata.SetValueComparer(shareTargetListComparer);

            entity.HasIndex(p => p.OwnerPartitionKey);

            // Self-managed optimistic-concurrency token (research.md D10, provider-portable) —
            // PromptService regenerates this on every successful write; EF Core includes the
            // original value in every write's concurrency check automatically, and a mismatch throws
            // DbUpdateConcurrencyException. IsConcurrencyToken() (not IsRowVersion(), which relies
            // on SQL Server's native auto-generated rowversion column type — not emulated
            // identically by every EF Core provider, including the InMemory provider used in tests).
            entity.Property(p => p.RowVersion).IsConcurrencyToken();
        });

        modelBuilder.Entity<PromptFavorite>(entity =>
        {
            // Composite key: makes double-favoriting idempotent by constraint, and makes one user's
            // favorites structurally incapable of appearing in another's list (SC-007).
            entity.HasKey(f => new { f.UserPartitionKey, f.PromptId });

            entity.HasIndex(f => f.PromptId);

            // FR-017: deleting a prompt removes every user's favorite of it via a database-level
            // cascade, so SC-006's "0 dangling references" is a property of the schema rather than
            // of cleanup logic at each delete call site (Constitution Principle V).
            entity.HasOne<PromptModel>()
                .WithMany()
                .HasForeignKey(f => f.PromptId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
