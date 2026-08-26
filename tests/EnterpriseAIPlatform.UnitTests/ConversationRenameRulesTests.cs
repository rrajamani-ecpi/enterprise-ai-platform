using EnterpriseAIPlatform.Application.Chat;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>Spec 024 US3 FR-007: a conversation rename must be rejected if empty or whitespace-only.</summary>
public class ConversationRenameRulesTests
{
    [Fact]
    public void TryValidate_NonEmptyName_Succeeds()
    {
        var result = ConversationRenameRules.TryValidate("Trip planning", out var trimmed);

        Assert.True(result);
        Assert.Equal("Trip planning", trimmed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryValidate_EmptyOrWhitespaceOrNull_IsRejected(string? candidate)
    {
        var result = ConversationRenameRules.TryValidate(candidate, out var trimmed);

        Assert.False(result);
        Assert.Equal(string.Empty, trimmed);
    }

    [Fact]
    public void TryValidate_TrimsLeadingAndTrailingWhitespace()
    {
        var result = ConversationRenameRules.TryValidate("  Trip planning  ", out var trimmed);

        Assert.True(result);
        Assert.Equal("Trip planning", trimmed);
    }
}
