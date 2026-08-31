using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.Prompts;
using EnterpriseAIPlatform.Domain.Identity;
using EnterpriseAIPlatform.Domain.Prompts;

namespace EnterpriseAIPlatform.UnitTests.Prompts;

/// <summary>
/// Spec 016 SC-004 — the full {owner, admin, collaborator, individual-share, group-share,
/// unrelated} x {read, write, transfer} matrix, exercised with no host and no infrastructure.
/// </summary>
public class PromptAccessEvaluatorTests
{
    private const string OwnerKey = "hash:owner";
    private const string CollaboratorKey = "hash:collaborator";
    private const string ShareeKey = "hash:sharee";
    private const string StrangerKey = "hash:stranger";
    private const string GroupToken = "@employees";

    private static PromptModel Prompt() => new()
    {
        Id = "p1",
        OwnerUserId = "owner@co.com",
        OwnerPartitionKey = OwnerKey,
        Name = "A",
        Description = "B",
        CollaboratorPartitionKeys = new List<string> { CollaboratorKey },
        SharedWith = new List<PromptShareTarget>
        {
            PromptShareTarget.ForIndividual(ShareeKey),
            PromptShareTarget.ForGroup(GroupToken),
        },
    };

    private static UserModel User(bool isAdmin = false, params string[] groupTokens) => new()
    {
        Name = "U",
        Email = "u@co.com",
        Roles = new RoleFlags(isAdmin, true, false, false),
        GroupTokens = groupTokens,
    };

    // --- Owner ---

    [Fact]
    public void Owner_HasReadWriteAndTransfer()
    {
        var prompt = Prompt();
        var owner = User();

        Assert.True(PromptAccessEvaluator.CanRead(prompt, owner, OwnerKey));
        Assert.True(PromptAccessEvaluator.CanWrite(prompt, owner, OwnerKey));
        Assert.True(PromptAccessEvaluator.CanTransfer(prompt, owner, OwnerKey));
    }

    // --- Admin (FR-001 grants write; FR-006 grants transfer) ---

    [Fact]
    public void Admin_HasReadWriteAndTransfer_EvenWithNoRelationshipToThePrompt()
    {
        var prompt = Prompt();
        var admin = User(isAdmin: true);

        Assert.True(PromptAccessEvaluator.CanRead(prompt, admin, StrangerKey));
        Assert.True(PromptAccessEvaluator.CanWrite(prompt, admin, StrangerKey));
        Assert.True(PromptAccessEvaluator.CanTransfer(prompt, admin, StrangerKey));
    }

    // --- Collaborator: write yes, transfer no (FR-006 names only owner and admin) ---

    [Fact]
    public void Collaborator_HasReadAndWrite_ButNotTransfer()
    {
        var prompt = Prompt();
        var collaborator = User();

        Assert.True(PromptAccessEvaluator.CanRead(prompt, collaborator, CollaboratorKey));
        Assert.True(PromptAccessEvaluator.CanWrite(prompt, collaborator, CollaboratorKey));
        Assert.False(PromptAccessEvaluator.CanTransfer(prompt, collaborator, CollaboratorKey));
    }

    // --- Share targets are strictly read-only (FR-002) ---

    [Fact]
    public void IndividualShareTarget_HasReadOnly()
    {
        var prompt = Prompt();
        var sharee = User();

        Assert.True(PromptAccessEvaluator.CanRead(prompt, sharee, ShareeKey));
        Assert.False(PromptAccessEvaluator.CanWrite(prompt, sharee, ShareeKey));
        Assert.False(PromptAccessEvaluator.CanTransfer(prompt, sharee, ShareeKey));
    }

    [Fact]
    public void GroupShareTarget_HasReadOnly_WhenCallerCarriesTheToken()
    {
        var prompt = Prompt();
        var member = User(isAdmin: false, GroupToken);

        Assert.True(PromptAccessEvaluator.CanRead(prompt, member, StrangerKey));
        Assert.False(PromptAccessEvaluator.CanWrite(prompt, member, StrangerKey));
        Assert.False(PromptAccessEvaluator.CanTransfer(prompt, member, StrangerKey));
    }

    [Fact]
    public void GroupShareTarget_DeniesReadWhenCallerCarriesADifferentToken()
    {
        var prompt = Prompt();
        var outsider = User(isAdmin: false, "@contractors");

        Assert.False(PromptAccessEvaluator.CanRead(prompt, outsider, StrangerKey));
    }

    [Fact]
    public void GroupShareTarget_DeniesReadWhenCallerCarriesNoTokens()
    {
        // The UserModel.GroupTokens default is empty (research.md D6) — a caller from a deployment
        // with no group claims must never fall through to allowed.
        var prompt = Prompt();
        var noGroups = User();

        Assert.False(PromptAccessEvaluator.CanRead(prompt, noGroups, StrangerKey));
    }

    // --- Unrelated caller ---

    [Fact]
    public void UnrelatedUser_IsDeniedEverything()
    {
        var prompt = Prompt();
        var stranger = User();

        Assert.False(PromptAccessEvaluator.CanRead(prompt, stranger, StrangerKey));
        Assert.False(PromptAccessEvaluator.CanWrite(prompt, stranger, StrangerKey));
        Assert.False(PromptAccessEvaluator.CanTransfer(prompt, stranger, StrangerKey));
    }

    [Fact]
    public void UnsharedPrompt_DeniesEveryoneButOwnerCollaboratorAndAdmin()
    {
        var prompt = Prompt();
        prompt.SharedWith.Clear();

        Assert.False(PromptAccessEvaluator.CanRead(prompt, User(), ShareeKey));
        Assert.False(PromptAccessEvaluator.CanRead(prompt, User(isAdmin: false, GroupToken), StrangerKey));
        Assert.True(PromptAccessEvaluator.CanRead(prompt, User(), OwnerKey));
        Assert.True(PromptAccessEvaluator.CanRead(prompt, User(), CollaboratorKey));
        Assert.True(PromptAccessEvaluator.CanRead(prompt, User(isAdmin: true), StrangerKey));
    }

    [Fact]
    public void IsPublished_DoesNotGrantAccess()
    {
        // The legacy flag is retained for migration fidelity only and must never be consulted by an
        // access decision (data-model.md).
        var prompt = Prompt();
        prompt.SharedWith.Clear();
        prompt.IsPublished = true;

        Assert.False(PromptAccessEvaluator.CanRead(prompt, User(), StrangerKey));
    }
}
