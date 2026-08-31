using System.Net;
using System.Net.Http.Json;
using static EnterpriseAIPlatform.IntegrationTests.Prompts.PromptTestClient;

namespace EnterpriseAIPlatform.IntegrationTests.Prompts;

/// <summary>
/// Spec 016 SC-001 / FR-005 — a transfer request changes ownership and nothing else. Each case
/// posts a transfer body carrying forged non-ownership fields and asserts every one of them is
/// unchanged afterwards. <c>TransferPromptOwnershipRequest</c> declares exactly one property, so
/// these values are unbindable before any handler code runs; these tests are the regression guard
/// that keeps that structural property true.
/// </summary>
public class PromptTransferFieldInjectionTests : IClassFixture<PromptWebApplicationFactory>
{
    private readonly PromptWebApplicationFactory _factory;

    public PromptTransferFieldInjectionTests(PromptWebApplicationFactory factory) => _factory = factory;

    private async Task<(PromptPayload Original, HttpClient Owner)> SeedAsync(string ownerEmail)
    {
        var owner = _factory.AsUser(ownerEmail);
        var prompt = await owner.CreatePromptAsync(Draft(
            "Canonical name", "Canonical text.",
            collaborators: new List<string> { "collab-known@co.com" },
            sharedWith: new List<ShareTargetPayload> { new("Individual", "sharee-known@co.com", null) }));

        return (prompt, owner);
    }

    private static void AssertOnlyOwnershipChanged(PromptPayload before, PromptPayload after, string expectedNewOwner)
    {
        Assert.Equal(expectedNewOwner, after.OwnerUserId);

        // FR-008: the identity of the prompt survives a transfer — no delete-then-recreate.
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.CreatedAtUtc, after.CreatedAtUtc);

        // FR-005: nothing else moved.
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.Description, after.Description);
        Assert.Equal(before.CollaboratorPartitionKeys, after.CollaboratorPartitionKeys);
        Assert.Equal(before.SharedWith.Count, after.SharedWith.Count);
    }

    public static TheoryData<string, object> ForgedBodies() => new()
    {
        { "forged name", new { newOwnerEmail = "new-owner@co.com", name = "HIJACKED" } },
        { "forged description", new { newOwnerEmail = "new-owner@co.com", description = "HIJACKED TEXT" } },
        { "forged createdAt", new { newOwnerEmail = "new-owner@co.com", createdAtUtc = "1990-01-01T00:00:00Z" } },
        { "forged sharedWith", new { newOwnerEmail = "new-owner@co.com", sharedWith = new[] { new { type = "Individual", identity = "attacker@co.com", groupToken = (string?)null } } } },
        { "forged ownerUserId", new { newOwnerEmail = "new-owner@co.com", ownerUserId = "attacker@co.com" } },
        { "forged ownerPartitionKey", new { newOwnerEmail = "new-owner@co.com", ownerPartitionKey = "attacker-hash" } },
        { "forged collaborators", new { newOwnerEmail = "new-owner@co.com", collaboratorPartitionKeys = new[] { "attacker-hash" } } },
        { "forged id", new { newOwnerEmail = "new-owner@co.com", id = "00000000000000000000000000000000" } },
        { "forged rowVersion", new { newOwnerEmail = "new-owner@co.com", rowVersion = "11111111-1111-1111-1111-111111111111" } },
        { "forged isPublished", new { newOwnerEmail = "new-owner@co.com", isPublished = true } },
    };

    [Theory]
    [MemberData(nameof(ForgedBodies))]
    public async Task Transfer_IgnoresEveryForgedNonOwnershipField(string caseName, object body)
    {
        var ownerEmail = $"inject-{caseName.Replace(' ', '-')}@co.com";
        var (before, owner) = await SeedAsync(ownerEmail);

        var response = await owner.PostAsJsonAsync($"/api/prompts/{before.Id}/transfer-ownership", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = (await response.Content.ReadFromJsonAsync<PromptPayload>())!;
        AssertOnlyOwnershipChanged(before, after, "new-owner@co.com");

        // Re-read from the database as the new owner, so this asserts persisted state rather than
        // just the response projection.
        var newOwner = _factory.AsUser("new-owner@co.com");
        var persisted = await newOwner.GetFromJsonAsync<PromptPayload>($"/api/prompts/{before.Id}");
        AssertOnlyOwnershipChanged(before, persisted!, "new-owner@co.com");
    }

    [Fact]
    public async Task Transfer_ByNonOwnerNonAdmin_IsRejectedWithNoWriteAtAll()
    {
        var (before, owner) = await SeedAsync("inject-owner-guard@co.com");
        var stranger = _factory.AsUser("inject-stranger@co.com");

        var response = await stranger.PostAsJsonAsync(
            $"/api/prompts/{before.Id}/transfer-ownership",
            new { newOwnerEmail = "inject-stranger@co.com" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var after = await owner.GetFromJsonAsync<PromptPayload>($"/api/prompts/{before.Id}");
        Assert.Equal("inject-owner-guard@co.com", after!.OwnerUserId);
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
    }

    [Fact]
    public async Task Transfer_ByCollaborator_IsRejected()
    {
        // FR-006 is narrower than FR-001's write gate: a collaborator may edit the prompt but may
        // never give it away.
        var owner = _factory.AsUser("inject-owner-collab@co.com");
        var before = await owner.CreatePromptAsync(Draft(
            "Canonical name", "Canonical text.",
            collaborators: new List<string> { "inject-collab@co.com" }));

        var collaborator = _factory.AsUser("inject-collab@co.com");

        // Prove the collaborator really does have write access, so the transfer rejection below is
        // specifically about transfer and not about a missing write grant.
        Assert.Equal(
            HttpStatusCode.OK,
            (await collaborator.PatchAsJsonAsync($"/api/prompts/{before.Id}", Draft("Edited", "Edited text.",
                collaborators: new List<string> { "inject-collab@co.com" }))).StatusCode);

        var response = await collaborator.PostAsJsonAsync(
            $"/api/prompts/{before.Id}/transfer-ownership",
            new { newOwnerEmail = "inject-collab@co.com" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var after = await owner.GetFromJsonAsync<PromptPayload>($"/api/prompts/{before.Id}");
        Assert.Equal("inject-owner-collab@co.com", after!.OwnerUserId);
    }

    [Fact]
    public async Task Transfer_ByAdmin_IsAllowed()
    {
        var (before, _) = await SeedAsync("inject-owner-adminok@co.com");
        var admin = _factory.AsUser("inject-admin@co.com", isAdmin: true);

        var response = await admin.PostAsJsonAsync(
            $"/api/prompts/{before.Id}/transfer-ownership",
            new { newOwnerEmail = "admin-picked@co.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("admin-picked@co.com", (await response.Content.ReadFromJsonAsync<PromptPayload>())!.OwnerUserId);
    }

    [Fact]
    public async Task Transfer_ByOwner_IsAllowedEvenThoughOwnerIsNotAdmin()
    {
        // Guards against regressing to spec 009's route-level RequireAdmin, which would reject the
        // owner outright.
        var (before, owner) = await SeedAsync("inject-plain-owner@co.com");

        var response = await owner.PostAsJsonAsync(
            $"/api/prompts/{before.Id}/transfer-ownership",
            new { newOwnerEmail = "owner-picked@co.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FormerOwner_LosesWriteAccessAfterTransfer()
    {
        var (before, owner) = await SeedAsync("inject-former@co.com");

        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync(
            $"/api/prompts/{before.Id}/transfer-ownership",
            new { newOwnerEmail = "inject-recipient@co.com" })).StatusCode);

        var response = await owner.PatchAsJsonAsync($"/api/prompts/{before.Id}", Draft("Still mine?", "Text."));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- T036: malformed recipient rejected before any write ---

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("@no-local-part.com")]
    [InlineData("no-domain@")]
    [InlineData("two@at@signs.com")]
    [InlineData("spaces in@email.com")]
    public async Task Transfer_RejectsMalformedRecipient_WithoutWriting(string recipient)
    {
        var (before, owner) = await SeedAsync($"inject-malformed-{recipient.GetHashCode():X}@co.com");

        var response = await owner.PostAsJsonAsync(
            $"/api/prompts/{before.Id}/transfer-ownership",
            new { newOwnerEmail = recipient });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var after = await owner.GetFromJsonAsync<PromptPayload>($"/api/prompts/{before.Id}");
        Assert.Equal(before.OwnerUserId, after!.OwnerUserId);
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
    }
}
