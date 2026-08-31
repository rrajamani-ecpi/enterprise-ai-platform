using System.Net;
using System.Net.Http.Json;
using static EnterpriseAIPlatform.IntegrationTests.Prompts.PromptTestClient;

namespace EnterpriseAIPlatform.IntegrationTests.Prompts;

/// <summary>
/// Spec 016 SC-007 / FR-016 / FR-019 — favorites are strictly per-user, idempotent in both
/// directions, gated by read access, and untouched by an ownership transfer.
/// </summary>
public class PromptFavoritesTests : IClassFixture<PromptWebApplicationFactory>
{
    private readonly PromptWebApplicationFactory _factory;

    public PromptFavoritesTests(PromptWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Favorite_IsVisibleToTheFavoritingUserOnly()
    {
        var userA = _factory.AsUser("fav-a@co.com");
        var userB = _factory.AsUser("fav-b@co.com");

        // Shared with B so both can read it — this isolates "not favorited by B" from
        // "not readable by B".
        var prompt = await userA.CreatePromptAsync(Draft(
            "Shared", "Text.",
            sharedWith: new List<ShareTargetPayload> { new("Individual", "fav-b@co.com", null) }));

        Assert.Equal(HttpStatusCode.OK, (await userA.PostAsync($"/api/prompts/{prompt.Id}/favorite", null)).StatusCode);

        var aFavorites = await userA.GetFromJsonAsync<List<PromptPayload>>("/api/prompts/favorites");
        Assert.Contains(aFavorites!, p => p.Id == prompt.Id);

        var bFavorites = await userB.GetFromJsonAsync<List<PromptPayload>>("/api/prompts/favorites");
        Assert.DoesNotContain(bFavorites!, p => p.Id == prompt.Id);

        // B can still read the prompt — only the favorite is private.
        Assert.Equal(HttpStatusCode.OK, (await userB.GetAsync($"/api/prompts/{prompt.Id}")).StatusCode);
    }

    [Fact]
    public async Task ListReflectsFavoriteFlagPerCaller()
    {
        var userA = _factory.AsUser("favflag-a@co.com");
        var userB = _factory.AsUser("favflag-b@co.com");

        var prompt = await userA.CreatePromptAsync(Draft(
            "Flagged", "Text.",
            sharedWith: new List<ShareTargetPayload> { new("Individual", "favflag-b@co.com", null) }));

        await userA.PostAsync($"/api/prompts/{prompt.Id}/favorite", null);

        var aList = await userA.GetFromJsonAsync<List<ListItemPayload>>("/api/prompts");
        Assert.True(aList!.Single(p => p.Prompt.Id == prompt.Id).IsFavorite);

        var bList = await userB.GetFromJsonAsync<List<ListItemPayload>>("/api/prompts");
        Assert.False(bList!.Single(p => p.Prompt.Id == prompt.Id).IsFavorite);
    }

    [Fact]
    public async Task RepeatFavorite_IsIdempotent()
    {
        var user = _factory.AsUser("fav-idem@co.com");
        var prompt = await user.CreatePromptAsync(Draft("Idem", "Text."));

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await user.PostAsync($"/api/prompts/{prompt.Id}/favorite", null)).StatusCode);
        }

        var favorites = await user.GetFromJsonAsync<List<PromptPayload>>("/api/prompts/favorites");
        Assert.Single(favorites!, p => p.Id == prompt.Id);
    }

    [Fact]
    public async Task RepeatUnfavorite_IsIdempotent()
    {
        var user = _factory.AsUser("unfav-idem@co.com");
        var prompt = await user.CreatePromptAsync(Draft("Idem", "Text."));

        await user.PostAsync($"/api/prompts/{prompt.Id}/favorite", null);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await user.DeleteAsync($"/api/prompts/{prompt.Id}/favorite")).StatusCode);
        }

        var favorites = await user.GetFromJsonAsync<List<PromptPayload>>("/api/prompts/favorites");
        Assert.DoesNotContain(favorites!, p => p.Id == prompt.Id);
    }

    [Fact]
    public async Task UnfavoriteWithoutFavoriting_Succeeds()
    {
        var user = _factory.AsUser("unfav-never@co.com");
        var prompt = await user.CreatePromptAsync(Draft("Never", "Text."));

        Assert.Equal(HttpStatusCode.OK, (await user.DeleteAsync($"/api/prompts/{prompt.Id}/favorite")).StatusCode);
    }

    [Fact]
    public async Task ConcurrentFavorites_DoNotCreateDuplicates()
    {
        // The composite primary key, not the pre-read, is what makes this safe.
        var user = _factory.AsUser("fav-concurrent@co.com");
        var prompt = await user.CreatePromptAsync(Draft("Concurrent", "Text."));

        await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => user.PostAsync($"/api/prompts/{prompt.Id}/favorite", null)));

        var favorites = await user.GetFromJsonAsync<List<PromptPayload>>("/api/prompts/favorites");
        Assert.Single(favorites!, p => p.Id == prompt.Id);
    }

    [Fact]
    public async Task Favoriting_APromptTheCallerCannotRead_Returns401()
    {
        var owner = _factory.AsUser("fav-owner@co.com");
        var stranger = _factory.AsUser("fav-stranger@co.com");

        var prompt = await owner.CreatePromptAsync(Draft("Private", "Text."));

        var response = await stranger.PostAsync($"/api/prompts/{prompt.Id}/favorite", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var favorites = await stranger.GetFromJsonAsync<List<PromptPayload>>("/api/prompts/favorites");
        Assert.Empty(favorites!);
    }

    [Fact]
    public async Task OneUserCannotRemoveAnothersFavorite()
    {
        var userA = _factory.AsUser("favdel-a@co.com");
        var userB = _factory.AsUser("favdel-b@co.com");

        var prompt = await userA.CreatePromptAsync(Draft(
            "Shared", "Text.",
            sharedWith: new List<ShareTargetPayload> { new("Individual", "favdel-b@co.com", null) }));

        await userA.PostAsync($"/api/prompts/{prompt.Id}/favorite", null);

        // B's delete is scoped to B's own composite-key row, so it cannot touch A's.
        Assert.Equal(HttpStatusCode.OK, (await userB.DeleteAsync($"/api/prompts/{prompt.Id}/favorite")).StatusCode);

        var aFavorites = await userA.GetFromJsonAsync<List<PromptPayload>>("/api/prompts/favorites");
        Assert.Contains(aFavorites!, p => p.Id == prompt.Id);
    }

    // --- FR-019: a transfer must not disturb anyone's favorites, including the former owner's. ---

    [Fact]
    public async Task OwnershipTransfer_LeavesEveryUsersFavoritesUntouched()
    {
        var owner = _factory.AsUser("favxfer-owner@co.com");
        var sharee = _factory.AsUser("favxfer-sharee@co.com");
        var recipient = _factory.AsUser("favxfer-recipient@co.com");

        var prompt = await owner.CreatePromptAsync(Draft(
            "Transferring", "Text.",
            sharedWith: new List<ShareTargetPayload>
            {
                // The owner shares with themselves too, so that after the transfer they still have
                // read access — this isolates "favorite survived the transfer" from "lost read
                // access", which would hide the favorite for an unrelated reason.
                new("Individual", "favxfer-owner@co.com", null),
                new("Individual", "favxfer-sharee@co.com", null),
                new("Individual", "favxfer-recipient@co.com", null),
            }));

        await owner.PostAsync($"/api/prompts/{prompt.Id}/favorite", null);
        await sharee.PostAsync($"/api/prompts/{prompt.Id}/favorite", null);

        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync(
            $"/api/prompts/{prompt.Id}/transfer-ownership",
            new { newOwnerEmail = "favxfer-recipient@co.com" })).StatusCode);

        // FR-019: the former owner's favorite is untouched by the transfer.
        var ownerFavorites = await owner.GetFromJsonAsync<List<PromptPayload>>("/api/prompts/favorites");
        Assert.Contains(ownerFavorites!, p => p.Id == prompt.Id);

        var shareeFavorites = await sharee.GetFromJsonAsync<List<PromptPayload>>("/api/prompts/favorites");
        Assert.Contains(shareeFavorites!, p => p.Id == prompt.Id);

        // The new owner never favorited it, and the transfer did not favorite it for them.
        var recipientFavorites = await recipient.GetFromJsonAsync<List<PromptPayload>>("/api/prompts/favorites");
        Assert.DoesNotContain(recipientFavorites!, p => p.Id == prompt.Id);
    }
}
