using System.Net;
using System.Net.Http.Json;
using static EnterpriseAIPlatform.IntegrationTests.Prompts.PromptTestClient;

namespace EnterpriseAIPlatform.IntegrationTests.Prompts;

/// <summary>
/// Spec 016 SC-004 — a read grant never becomes a write grant. Each case asserts both the rejection
/// and that the stored row is byte-for-byte unchanged afterwards, so a denial that returns 401 while
/// still applying the write would fail.
/// </summary>
public class PromptWriteAuthorizationTests : IClassFixture<PromptWebApplicationFactory>
{
    private readonly PromptWebApplicationFactory _factory;

    public PromptWriteAuthorizationTests(PromptWebApplicationFactory factory) => _factory = factory;

    private async Task<(PromptPayload Prompt, HttpClient Owner)> SharedPromptAsync(string ownerEmail, string shareeEmail)
    {
        var owner = _factory.AsUser(ownerEmail);
        var prompt = await owner.CreatePromptAsync(Draft(
            "Original name", "Original text.",
            sharedWith: new List<ShareTargetPayload> { new("Individual", shareeEmail, null) }));

        return (prompt, owner);
    }

    private async Task AssertUnchangedAsync(HttpClient owner, PromptPayload original)
    {
        var current = await owner.GetFromJsonAsync<PromptPayload>($"/api/prompts/{original.Id}");
        Assert.Equal(original.Name, current!.Name);
        Assert.Equal(original.Description, current.Description);
        Assert.Equal(original.OwnerUserId, current.OwnerUserId);
        Assert.Equal(original.UpdatedAtUtc, current.UpdatedAtUtc);
    }

    [Fact]
    public async Task ShareTarget_CannotUpdate()
    {
        var (prompt, owner) = await SharedPromptAsync("wauth-owner1@co.com", "wauth-sharee1@co.com");
        var sharee = _factory.AsUser("wauth-sharee1@co.com");

        var response = await sharee.PatchAsJsonAsync($"/api/prompts/{prompt.Id}", Draft("Hijacked", "Hijacked text."));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertUnchangedAsync(owner, prompt);
    }

    [Fact]
    public async Task ShareTarget_CannotDelete()
    {
        var (prompt, owner) = await SharedPromptAsync("wauth-owner2@co.com", "wauth-sharee2@co.com");
        var sharee = _factory.AsUser("wauth-sharee2@co.com");

        var response = await sharee.DeleteAsync($"/api/prompts/{prompt.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertUnchangedAsync(owner, prompt);
    }

    [Fact]
    public async Task GroupShareTarget_CannotUpdate()
    {
        var owner = _factory.AsUser("wauth-owner-grp@co.com", isAdmin: true);
        var prompt = await owner.CreatePromptAsync(Draft(
            "Original name", "Original text.",
            sharedWith: new List<ShareTargetPayload> { new("Group", null, "engineering-all") }));

        var member = _factory.AsUser("wauth-member@co.com", groups: "engineering-all");

        // The group member can read it...
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/prompts/{prompt.Id}")).StatusCode);

        // ...but the read grant confers no write (FR-002).
        var response = await member.PatchAsJsonAsync($"/api/prompts/{prompt.Id}", Draft("Hijacked", "Hijacked text."));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertUnchangedAsync(owner, prompt);
    }

    [Fact]
    public async Task UnrelatedUser_CannotUpdate()
    {
        var owner = _factory.AsUser("wauth-owner3@co.com");
        var prompt = await owner.CreatePromptAsync(Draft("Original name", "Original text."));
        var stranger = _factory.AsUser("wauth-stranger@co.com");

        var response = await stranger.PatchAsJsonAsync($"/api/prompts/{prompt.Id}", Draft("Hijacked", "Hijacked text."));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertUnchangedAsync(owner, prompt);
    }

    [Fact]
    public async Task UnrelatedUser_CannotDelete()
    {
        var owner = _factory.AsUser("wauth-owner4@co.com");
        var prompt = await owner.CreatePromptAsync(Draft("Original name", "Original text."));
        var stranger = _factory.AsUser("wauth-stranger4@co.com");

        var response = await stranger.DeleteAsync($"/api/prompts/{prompt.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertUnchangedAsync(owner, prompt);
    }

    [Fact]
    public async Task UnauthenticatedCaller_IsRejected()
    {
        var owner = _factory.AsUser("wauth-owner5@co.com");
        var prompt = await owner.CreatePromptAsync(Draft("Original name", "Original text."));

        // No X-Test-User header at all.
        var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync($"/api/prompts/{prompt.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_IgnoresAnyClientSuppliedOwner()
    {
        // The write contract has no owner property at all, so a forged owner cannot even be bound.
        // This asserts the resulting owner is always the authenticated caller (Principle II).
        var client = _factory.AsUser("wauth-realowner@co.com");

        var response = await client.PostAsJsonAsync("/api/prompts", new
        {
            name = "Forged",
            description = "Text.",
            collaboratorEmails = new List<string>(),
            sharedWith = new List<object>(),
            ownerUserId = "victim@co.com",
            ownerPartitionKey = "victim-hash",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<PromptPayload>())!;
        Assert.Equal("wauth-realowner@co.com", created.OwnerUserId);
    }
}
