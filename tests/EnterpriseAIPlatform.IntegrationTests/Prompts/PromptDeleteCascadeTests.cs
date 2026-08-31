using System.Net;
using System.Net.Http.Json;
using EnterpriseAIPlatform.Infrastructure.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static EnterpriseAIPlatform.IntegrationTests.Prompts.PromptTestClient;

namespace EnterpriseAIPlatform.IntegrationTests.Prompts;

/// <summary>
/// Spec 016 SC-006 / FR-017 — deleting a prompt leaves zero dangling favorite references. The
/// removal comes from the schema-level FK cascade defined in <see cref="PromptDbContext"/>, not
/// from cleanup code in <c>DeleteAsync</c>, so it cannot be forgotten at a future delete call site
/// (Constitution Principle V).
/// </summary>
public class PromptDeleteCascadeTests : IClassFixture<PromptWebApplicationFactory>
{
    private readonly PromptWebApplicationFactory _factory;

    public PromptDeleteCascadeTests(PromptWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task DeletingAPrompt_RemovesItFromEveryUsersFavorites_AndLeavesNoRows()
    {
        var owner = _factory.AsUser("cascade-owner@co.com");
        var userB = _factory.AsUser("cascade-b@co.com");
        var userC = _factory.AsUser("cascade-c@co.com");

        var prompt = await owner.CreatePromptAsync(Draft(
            "Doomed", "Text.",
            sharedWith: new List<ShareTargetPayload>
            {
                new("Individual", "cascade-b@co.com", null),
                new("Individual", "cascade-c@co.com", null),
            }));

        foreach (var client in new[] { owner, userB, userC })
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/prompts/{prompt.Id}/favorite", null)).StatusCode);
        }

        // Sanity: all three favorites really exist before the delete.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PromptDbContext>();
            Assert.Equal(3, await db.PromptFavorites.CountAsync(f => f.PromptId == prompt.Id));
        }

        Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync($"/api/prompts/{prompt.Id}")).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PromptDbContext>();
            Assert.False(await db.Prompts.AnyAsync(p => p.Id == prompt.Id));
        }

        // And it is gone from every surface, for every user.
        foreach (var client in new[] { owner, userB, userC })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/prompts/{prompt.Id}")).StatusCode);

            var list = await client.GetFromJsonAsync<List<ListItemPayload>>("/api/prompts");
            Assert.DoesNotContain(list!, p => p.Prompt.Id == prompt.Id);

            var favorites = await client.GetFromJsonAsync<List<PromptPayload>>("/api/prompts/favorites");
            Assert.DoesNotContain(favorites!, p => p.Id == prompt.Id);
        }
    }

    /// <summary>
    /// SC-006's "zero dangling rows" guarantee, asserted against the model rather than by counting
    /// rows after a delete. The EF Core InMemory provider used by these tests has no referential
    /// integrity engine and never performs a store-level cascade, so a row count here would fail
    /// against a schema that is in fact correct. The cascade is a property of the mapping (and of
    /// the generated <c>ON DELETE CASCADE</c>), which is exactly what this asserts — and asserting
    /// it at the model level also means it holds for every delete call site, not just the one this
    /// test happens to exercise.
    /// </summary>
    [Fact]
    public void FavoritesForeignKey_IsConfiguredToCascadeOnPromptDelete()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromptDbContext>();

        var favoriteEntity = db.Model.FindEntityType(typeof(EnterpriseAIPlatform.Domain.Prompts.PromptFavorite))!;
        var foreignKey = Assert.Single(favoriteEntity.GetForeignKeys());

        Assert.Equal(typeof(EnterpriseAIPlatform.Domain.Prompts.PromptModel), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void FavoritesPrimaryKey_IsTheUserPromptPair()
    {
        // What makes favoriting idempotent, and makes one user's favorites structurally incapable
        // of appearing in another's list (SC-007).
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromptDbContext>();

        var favoriteEntity = db.Model.FindEntityType(typeof(EnterpriseAIPlatform.Domain.Prompts.PromptFavorite))!;
        var key = favoriteEntity.FindPrimaryKey()!;

        Assert.Equal(
            new[] { "PromptId", "UserPartitionKey" },
            key.Properties.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task DeletingOnePrompt_DoesNotDisturbFavoritesOfAnother()
    {
        var user = _factory.AsUser("cascade-scope@co.com");

        var doomed = await user.CreatePromptAsync(Draft("Doomed", "Text."));
        var survivor = await user.CreatePromptAsync(Draft("Survivor", "Text."));

        await user.PostAsync($"/api/prompts/{doomed.Id}/favorite", null);
        await user.PostAsync($"/api/prompts/{survivor.Id}/favorite", null);

        Assert.Equal(HttpStatusCode.OK, (await user.DeleteAsync($"/api/prompts/{doomed.Id}")).StatusCode);

        var favorites = await user.GetFromJsonAsync<List<PromptPayload>>("/api/prompts/favorites");
        Assert.DoesNotContain(favorites!, p => p.Id == doomed.Id);
        Assert.Contains(favorites!, p => p.Id == survivor.Id);
    }
}
