using EnterpriseAIPlatform.Domain.Sharing;

namespace EnterpriseAIPlatform.UnitTests.Sharing;

/// <summary>Spec 018 US1/US4 — FR-001/FR-012/FR-013: ShareTarget's construction invariants.</summary>
public class ShareTargetTests
{
    [Fact]
    public void ForIndividual_SetsIdentity_LeavesGroupTokenNull()
    {
        var target = ShareTarget.ForIndividual("student@contoso.com");

        Assert.Equal(ShareTargetType.Individual, target.Type);
        Assert.Equal("student@contoso.com", target.Identity);
        Assert.Null(target.GroupToken);
    }

    [Fact]
    public void ForGroup_SetsGroupToken_LeavesIdentityNull()
    {
        var target = ShareTarget.ForGroup("students");

        Assert.Equal(ShareTargetType.Group, target.Type);
        Assert.Equal("students", target.GroupToken);
        Assert.Null(target.Identity);
    }

    [Fact]
    public void DefaultsToReadAccess_WhenNotExplicitlyDesignatedCollaborator()
    {
        var individual = ShareTarget.ForIndividual("student@contoso.com");
        var group = ShareTarget.ForGroup("students");

        Assert.Equal(AccessLevel.Read, individual.AccessLevel);
        Assert.Equal(AccessLevel.Read, group.AccessLevel);
    }

    [Fact]
    public void RetainsExplicitCollaboratorDesignation()
    {
        var target = ShareTarget.ForIndividual("faculty@contoso.com", AccessLevel.Collaborator);

        Assert.Equal(AccessLevel.Collaborator, target.AccessLevel);
    }
}
