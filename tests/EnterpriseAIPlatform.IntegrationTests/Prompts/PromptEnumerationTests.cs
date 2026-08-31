using System.Net;
using System.Net.Http.Json;
using static EnterpriseAIPlatform.IntegrationTests.Prompts.PromptTestClient;

namespace EnterpriseAIPlatform.IntegrationTests.Prompts;

/// <summary>
/// Spec 016 FR-009 — an unauthorized caller must not be able to tell a prompt they cannot see from
/// one that has never existed. Each test compares a forbidden-but-real id against a fabricated id
/// and asserts the two responses are indistinguishable, which is the property that actually blocks
/// id enumeration (a matching status code alone would not).
/// </summary>
public class PromptEnumerationTests : IClassFixture<PromptWebApplicationFactory>
{
    private const string NeverExistedId = "ffffffffffffffffffffffffffffffff";

    private readonly PromptWebApplicationFactory _factory;

    public PromptEnumerationTests(PromptWebApplicationFactory factory) => _factory = factory;

    private async Task<string> ForbiddenPromptIdAsync(string ownerEmail)
    {
        var owner = _factory.AsUser(ownerEmail);
        var prompt = await owner.CreatePromptAsync(Draft("Secret", "Confidential text."));
        return prompt.Id;
    }

    private static async Task AssertIndistinguishableAsync(HttpResponseMessage forbidden, HttpResponseMessage missing)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, forbidden.StatusCode);
        Assert.Equal(missing.StatusCode, forbidden.StatusCode);

        var forbiddenBody = await forbidden.Content.ReadAsStringAsync();
        var missingBody = await missing.Content.ReadAsStringAsync();
        Assert.Equal(missingBody, forbiddenBody);

        // A differing header (e.g. Content-Length, or a WWW-Authenticate present on only one) would
        // be just as usable an oracle as a differing status code.
        Assert.Equal(
            missing.Headers.Select(h => h.Key).OrderBy(k => k, StringComparer.Ordinal),
            forbidden.Headers.Select(h => h.Key).OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(
            missing.Content.Headers.Select(h => h.Key).OrderBy(k => k, StringComparer.Ordinal),
            forbidden.Content.Headers.Select(h => h.Key).OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Get_ForbiddenAndNonexistent_AreIndistinguishable()
    {
        var forbiddenId = await ForbiddenPromptIdAsync("enum-owner1@co.com");
        var stranger = _factory.AsUser("enum-stranger1@co.com");

        var forbidden = await stranger.GetAsync($"/api/prompts/{forbiddenId}");
        var missing = await stranger.GetAsync($"/api/prompts/{NeverExistedId}");

        await AssertIndistinguishableAsync(forbidden, missing);
    }

    [Fact]
    public async Task Update_ForbiddenAndNonexistent_AreIndistinguishable()
    {
        var forbiddenId = await ForbiddenPromptIdAsync("enum-owner2@co.com");
        var stranger = _factory.AsUser("enum-stranger2@co.com");

        var forbidden = await stranger.PatchAsJsonAsync($"/api/prompts/{forbiddenId}", Draft());
        var missing = await stranger.PatchAsJsonAsync($"/api/prompts/{NeverExistedId}", Draft());

        await AssertIndistinguishableAsync(forbidden, missing);
    }

    [Fact]
    public async Task Delete_ForbiddenAndNonexistent_AreIndistinguishable()
    {
        var forbiddenId = await ForbiddenPromptIdAsync("enum-owner3@co.com");
        var stranger = _factory.AsUser("enum-stranger3@co.com");

        var forbidden = await stranger.DeleteAsync($"/api/prompts/{forbiddenId}");
        var missing = await stranger.DeleteAsync($"/api/prompts/{NeverExistedId}");

        await AssertIndistinguishableAsync(forbidden, missing);
    }

    [Fact]
    public async Task TransferOwnership_ForbiddenAndNonexistent_AreIndistinguishable()
    {
        // Spec 009's persona transfer returns 404 for a missing row; spec 016 deliberately does not,
        // because a 404-vs-401 split here is exactly the enumeration oracle FR-009 forbids.
        var forbiddenId = await ForbiddenPromptIdAsync("enum-owner4@co.com");
        var stranger = _factory.AsUser("enum-stranger4@co.com");
        var body = new { newOwnerEmail = "attacker@co.com" };

        var forbidden = await stranger.PostAsJsonAsync($"/api/prompts/{forbiddenId}/transfer-ownership", body);
        var missing = await stranger.PostAsJsonAsync($"/api/prompts/{NeverExistedId}/transfer-ownership", body);

        await AssertIndistinguishableAsync(forbidden, missing);
    }

    [Fact]
    public async Task Favorite_ForbiddenAndNonexistent_AreIndistinguishable()
    {
        var forbiddenId = await ForbiddenPromptIdAsync("enum-owner5@co.com");
        var stranger = _factory.AsUser("enum-stranger5@co.com");

        var forbidden = await stranger.PostAsync($"/api/prompts/{forbiddenId}/favorite", null);
        var missing = await stranger.PostAsync($"/api/prompts/{NeverExistedId}/favorite", null);

        await AssertIndistinguishableAsync(forbidden, missing);
    }

    [Fact]
    public async Task ReadOnlySharee_CannotDistinguishAForbiddenWriteFromAMissingPrompt()
    {
        // The subtler case: this caller CAN read the prompt, so they already know it exists. The
        // write denial must still not confirm anything beyond that — it must match the missing-id
        // response exactly, so the same response shape covers every denial reason.
        var owner = _factory.AsUser("enum-owner6@co.com");
        var prompt = await owner.CreatePromptAsync(Draft(
            "Readable", "Text.",
            sharedWith: new List<ShareTargetPayload> { new("Individual", "enum-sharee6@co.com", null) }));

        var sharee = _factory.AsUser("enum-sharee6@co.com");

        var forbidden = await sharee.PatchAsJsonAsync($"/api/prompts/{prompt.Id}", Draft());
        var missing = await sharee.PatchAsJsonAsync($"/api/prompts/{NeverExistedId}", Draft());

        await AssertIndistinguishableAsync(forbidden, missing);
    }

    [Fact]
    public async Task NoResponseEverCarriesA404ForAGatedPromptRoute()
    {
        // ToHttpResult has no NOT_FOUND branch by construction; this guards against a future edit
        // reintroducing one.
        var stranger = _factory.AsUser("enum-stranger7@co.com");

        foreach (var response in new[]
        {
            await stranger.GetAsync($"/api/prompts/{NeverExistedId}"),
            await stranger.DeleteAsync($"/api/prompts/{NeverExistedId}"),
            await stranger.PostAsync($"/api/prompts/{NeverExistedId}/favorite", null),
        })
        {
            Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
