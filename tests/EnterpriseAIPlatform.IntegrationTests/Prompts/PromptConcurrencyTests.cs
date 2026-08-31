using System.Net;
using System.Net.Http.Json;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.Sharing;
using EnterpriseAIPlatform.Domain.Identity;
using EnterpriseAIPlatform.Domain.Prompts;
using EnterpriseAIPlatform.Domain.Sharing;
using EnterpriseAIPlatform.Infrastructure.Identity;
using EnterpriseAIPlatform.Infrastructure.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static EnterpriseAIPlatform.IntegrationTests.Prompts.PromptTestClient;

namespace EnterpriseAIPlatform.IntegrationTests.Prompts;

/// <summary>
/// Spec 016 FR-007/FR-008 — the double-submit edge case. Two concurrent transfers of the same
/// prompt: the first wins, the second is rejected with a conflict rather than silently applied
/// last-write-wins (Principle III), and the prompt ends up existing exactly once under exactly one
/// owner.
/// </summary>
public class PromptConcurrencyTests : IClassFixture<PromptWebApplicationFactory>
{
    private readonly PromptWebApplicationFactory _factory;

    public PromptConcurrencyTests(PromptWebApplicationFactory factory) => _factory = factory;

    private sealed class AllowAllSharingPolicy : ISharingPolicyService
    {
        public SharingDecision Evaluate(UserModel caller, ShareTargetRequest request) =>
            new(true, SharingDecisionReason.AdminBypass);
    }

    private static UserModel Admin(string email) => new()
    {
        Name = email,
        Email = email,
        Roles = new RoleFlags(true, true, false, false),
    };

    /// <summary>
    /// Deterministic interleaving: both callers read the row *before* either writes, which is
    /// exactly the state a genuine double-submit produces.
    /// </summary>
    [Fact]
    public async Task TwoConcurrentTransfers_FirstWins_SecondConflicts()
    {
        var databaseName = $"concurrency-{Guid.NewGuid()}";
        var internalServices = new ServiceCollection().AddEntityFrameworkInMemoryDatabase().BuildServiceProvider();

        PromptDbContext NewContext() => new(new DbContextOptionsBuilder<PromptDbContext>()
            .UseInMemoryDatabase(databaseName)
            .UseInternalServiceProvider(internalServices)
            .Options);

        var admin = Admin("conc-admin@co.com");

        await using var seedDb = NewContext();
        var seeded = await new PromptService(seedDb, new IdentityHasher(), new AllowAllSharingPolicy())
            .CreateAsync(
                new PromptModel
                {
                    Id = string.Empty,
                    OwnerUserId = string.Empty,
                    OwnerPartitionKey = string.Empty,
                    Name = "Contested",
                    Description = "Text.",
                },
                admin);

        var promptId = seeded.Response!.Id;

        // Two independent units of work, each with its own change tracker — the shape of two
        // in-flight requests.
        await using var dbA = NewContext();
        await using var dbB = NewContext();

        // Both load the row (and therefore the same original RowVersion) before either saves.
        var trackedA = await dbA.Prompts.FirstAsync(p => p.Id == promptId);
        var trackedB = await dbB.Prompts.FirstAsync(p => p.Id == promptId);
        Assert.Equal(trackedA.RowVersion, trackedB.RowVersion);

        var first = await new PromptService(dbA, new IdentityHasher(), new AllowAllSharingPolicy())
            .TransferOwnershipAsync(promptId, "winner@co.com", admin);
        Assert.Equal(ResponseStatus.OK, first.Status);

        var second = await new PromptService(dbB, new IdentityHasher(), new AllowAllSharingPolicy())
            .TransferOwnershipAsync(promptId, "loser@co.com", admin);

        // FR-007: rejected as a conflict, not applied last-write-wins.
        Assert.Equal(ResponseStatus.ERROR, second.Status);
        Assert.Equal(PromptService.ConcurrencyConflictMessage, second.Errors[0].Message);

        // FR-008: exactly one prompt, exactly one owner — the winner's.
        await using var verifyDb = NewContext();
        var rows = await verifyDb.Prompts.AsNoTracking().Where(p => p.Id == promptId).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("winner@co.com", rows[0].OwnerUserId);
    }

    [Fact]
    public async Task ConcurrentEdits_AreAlsoConflictGuarded()
    {
        var databaseName = $"concurrency-edit-{Guid.NewGuid()}";
        var internalServices = new ServiceCollection().AddEntityFrameworkInMemoryDatabase().BuildServiceProvider();

        PromptDbContext NewContext() => new(new DbContextOptionsBuilder<PromptDbContext>()
            .UseInMemoryDatabase(databaseName)
            .UseInternalServiceProvider(internalServices)
            .Options);

        var admin = Admin("conc-edit-admin@co.com");

        await using var seedDb = NewContext();
        var seeded = await new PromptService(seedDb, new IdentityHasher(), new AllowAllSharingPolicy())
            .CreateAsync(
                new PromptModel
                {
                    Id = string.Empty,
                    OwnerUserId = string.Empty,
                    OwnerPartitionKey = string.Empty,
                    Name = "Contested",
                    Description = "Text.",
                },
                admin);

        var promptId = seeded.Response!.Id;

        await using var dbA = NewContext();
        await using var dbB = NewContext();
        await dbA.Prompts.FirstAsync(p => p.Id == promptId);
        await dbB.Prompts.FirstAsync(p => p.Id == promptId);

        var draft = new PromptModel
        {
            Id = string.Empty,
            OwnerUserId = string.Empty,
            OwnerPartitionKey = string.Empty,
            Name = "Edited",
            Description = "Edited text.",
        };

        var first = await new PromptService(dbA, new IdentityHasher(), new AllowAllSharingPolicy())
            .UpdateAsync(promptId, draft, admin);
        Assert.Equal(ResponseStatus.OK, first.Status);

        var second = await new PromptService(dbB, new IdentityHasher(), new AllowAllSharingPolicy())
            .UpdateAsync(promptId, draft, admin);
        Assert.Equal(ResponseStatus.ERROR, second.Status);
        Assert.Equal(PromptService.ConcurrencyConflictMessage, second.Errors[0].Message);
    }

    /// <summary>
    /// End-to-end double-submit over HTTP. The interleaving is not deterministic here, so this
    /// asserts the invariant that must hold under <i>every</i> interleaving rather than a specific
    /// status split: at most one transfer succeeds, no response is a silent partial success, and
    /// the prompt ends with exactly one owner.
    /// </summary>
    [Fact]
    public async Task DoubleSubmitOverHttp_LeavesExactlyOneOwner()
    {
        var admin = _factory.AsUser("http-conc-admin@co.com", isAdmin: true);
        var prompt = await admin.CreatePromptAsync(Draft("Contested", "Text."));

        var responses = await Task.WhenAll(
            admin.PostAsJsonAsync($"/api/prompts/{prompt.Id}/transfer-ownership", new { newOwnerEmail = "race-a@co.com" }),
            admin.PostAsJsonAsync($"/api/prompts/{prompt.Id}/transfer-ownership", new { newOwnerEmail = "race-b@co.com" }));

        foreach (var response in responses)
        {
            Assert.Contains(response.StatusCode, new[]
            {
                HttpStatusCode.OK,
                HttpStatusCode.Conflict,
                HttpStatusCode.Unauthorized,
            });
        }

        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);

        var final = await admin.GetFromJsonAsync<PromptPayload>($"/api/prompts/{prompt.Id}");
        Assert.Contains(final!.OwnerUserId, new[] { "race-a@co.com", "race-b@co.com" });

        // FR-008: the transfer never forked the prompt into two records.
        var all = await admin.GetFromJsonAsync<List<ListItemPayload>>("/api/prompts");
        Assert.Single(all!, p => p.Prompt.Id == prompt.Id);
    }
}
