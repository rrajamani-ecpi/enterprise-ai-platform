using System.Net;
using System.Net.Http.Json;
using static EnterpriseAIPlatform.IntegrationTests.Prompts.PromptTestClient;

namespace EnterpriseAIPlatform.IntegrationTests.Prompts;

/// <summary>
/// Spec 016 US4 / SC-005 — the full create → read → update → read → delete → read lifecycle for
/// each caller role that FR-001 grants write access to, plus FR-003 validation.
/// </summary>
public class PromptCrudTests : IClassFixture<PromptWebApplicationFactory>
{
    private readonly PromptWebApplicationFactory _factory;

    public PromptCrudTests(PromptWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Owner_CompletesFullCrudLifecycle()
    {
        var client = _factory.AsUser("owner-crud@co.com");

        var created = await client.CreatePromptAsync(Draft("Draft", "Original text."));
        Assert.Equal("owner-crud@co.com", created.OwnerUserId);
        Assert.NotEmpty(created.Id);

        var read = await client.GetAsync($"/api/prompts/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal("Original text.", (await read.Content.ReadFromJsonAsync<PromptPayload>())!.Description);

        var update = await client.PatchAsJsonAsync($"/api/prompts/{created.Id}", Draft("Renamed", "Updated text."));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var reread = await client.GetAsync($"/api/prompts/{created.Id}");
        var afterUpdate = (await reread.Content.ReadFromJsonAsync<PromptPayload>())!;
        Assert.Equal("Renamed", afterUpdate.Name);
        Assert.Equal("Updated text.", afterUpdate.Description);
        Assert.Equal(created.Id, afterUpdate.Id);
        Assert.Equal(created.CreatedAtUtc, afterUpdate.CreatedAtUtc);

        var delete = await client.DeleteAsync($"/api/prompts/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        // FR-009: after deletion the prompt is indistinguishable from one that never existed.
        var afterDelete = await client.GetAsync($"/api/prompts/{created.Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Admin_CanReadUpdateAndDeleteAnotherUsersPrompt()
    {
        var owner = _factory.AsUser("owner-admincase@co.com");
        var admin = _factory.AsUser("admin@co.com", isAdmin: true);

        var created = await owner.CreatePromptAsync(Draft("Owned", "By someone else."));

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/prompts/{created.Id}")).StatusCode);

        var update = await admin.PatchAsJsonAsync($"/api/prompts/{created.Id}", Draft("Admin edit", "Changed."));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        // FR-001: an admin's edit must not silently reassign ownership.
        var afterUpdate = (await update.Content.ReadFromJsonAsync<PromptPayload>())!;
        Assert.Equal("owner-admincase@co.com", afterUpdate.OwnerUserId);

        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/prompts/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Collaborator_CanReadAndUpdateButPromptRemainsOwned()
    {
        var owner = _factory.AsUser("owner-collab@co.com");
        var collaborator = _factory.AsUser("collab@co.com");

        var created = await owner.CreatePromptAsync(
            Draft("Shared work", "Team text.", collaborators: new List<string> { "collab@co.com" }));

        Assert.Equal(HttpStatusCode.OK, (await collaborator.GetAsync($"/api/prompts/{created.Id}")).StatusCode);

        var update = await collaborator.PatchAsJsonAsync(
            $"/api/prompts/{created.Id}",
            Draft("Collab edit", "Edited by collaborator.", collaborators: new List<string> { "collab@co.com" }));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("owner-collab@co.com", (await update.Content.ReadFromJsonAsync<PromptPayload>())!.OwnerUserId);
    }

    [Fact]
    public async Task List_ReturnsOnlyPromptsTheCallerMayRead()
    {
        var owner = _factory.AsUser("list-owner@co.com");
        var stranger = _factory.AsUser("list-stranger@co.com");

        var mine = await owner.CreatePromptAsync(Draft("Mine", "Private."));

        var visible = await stranger.GetFromJsonAsync<List<ListItemPayload>>("/api/prompts");
        Assert.DoesNotContain(visible!, p => p.Prompt.Id == mine.Id);

        var ownerView = await owner.GetFromJsonAsync<List<ListItemPayload>>("/api/prompts");
        Assert.Contains(ownerView!, p => p.Prompt.Id == mine.Id);
    }

    [Fact]
    public async Task ShareTarget_CanReadViaIndividualGrant()
    {
        var owner = _factory.AsUser("share-owner@co.com");
        var sharee = _factory.AsUser("sharee@co.com");

        var created = await owner.CreatePromptAsync(Draft(
            "Shared", "Read me.",
            sharedWith: new List<ShareTargetPayload> { new("Individual", "sharee@co.com", null) }));

        Assert.Equal(HttpStatusCode.OK, (await sharee.GetAsync($"/api/prompts/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task ShareTarget_CanReadViaGroupGrant()
    {
        var owner = _factory.AsUser("group-owner@co.com", isAdmin: true);
        var member = _factory.AsUser("group-member@co.com", groups: "engineering-all");
        var nonMember = _factory.AsUser("group-outsider@co.com", groups: "sales-all");

        var created = await owner.CreatePromptAsync(Draft(
            "Group shared", "Read me.",
            sharedWith: new List<ShareTargetPayload> { new("Group", null, "engineering-all") }));

        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/prompts/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await nonMember.GetAsync($"/api/prompts/{created.Id}")).StatusCode);
    }

    // --- FR-003 validation, enforced in the Application layer so a direct API caller is bound
    // identically to a UI user (Principle V). ---

    [Theory]
    [InlineData("", "valid description")]
    [InlineData("   ", "valid description")]
    [InlineData("valid name", "")]
    [InlineData("valid name", "   ")]
    public async Task Create_RejectsEmptyNameOrDescription(string name, string description)
    {
        var client = _factory.AsUser("validation@co.com");

        var response = await client.PostAsJsonAsync("/api/prompts", Draft(name, description));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_RejectsEmptyNameAndLeavesStoredRowUnchanged()
    {
        var client = _factory.AsUser("validation-update@co.com");
        var created = await client.CreatePromptAsync(Draft("Keep", "Keep this text."));

        var response = await client.PatchAsJsonAsync($"/api/prompts/{created.Id}", Draft("", "Replacement."));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var reread = await client.GetFromJsonAsync<PromptPayload>($"/api/prompts/{created.Id}");
        Assert.Equal("Keep", reread!.Name);
        Assert.Equal("Keep this text.", reread.Description);
    }
}
