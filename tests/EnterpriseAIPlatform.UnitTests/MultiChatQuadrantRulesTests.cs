using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Domain.Chat;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>Spec 006 FR-004/FR-005: quadrant count always stays in [2, 4].</summary>
public class MultiChatQuadrantRulesTests
{
    private static List<MultiChatQuadrant> Quadrants(int count) =>
        Enumerable.Range(0, count).Select(i => new MultiChatQuadrant { Position = i }).ToList();

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void TryAddQuadrant_BelowCap_Succeeds_AndIncrementsCount(int startingCount)
    {
        var quadrants = Quadrants(startingCount);

        var added = MultiChatQuadrantRules.TryAddQuadrant(quadrants);

        Assert.True(added);
        Assert.Equal(startingCount + 1, quadrants.Count);
    }

    [Fact]
    public void TryAddQuadrant_AtCap_IsRejected_CountStaysAtFour()
    {
        var quadrants = Quadrants(4);

        var added = MultiChatQuadrantRules.TryAddQuadrant(quadrants);

        Assert.False(added);
        Assert.Equal(4, quadrants.Count);
    }

    [Theory]
    [InlineData(4, 3)]
    [InlineData(3, 2)]
    public void RemoveQuadrant_AboveFloor_RemovesTheLastQuadrant(int startingCount, int expectedCount)
    {
        var quadrants = Quadrants(startingCount);

        MultiChatQuadrantRules.RemoveQuadrant(quadrants);

        Assert.Equal(expectedCount, quadrants.Count);
    }

    [Fact]
    public void RemoveQuadrant_AtFloor_ClearsAssignment_RatherThanRemovingTheSlot()
    {
        var quadrants = Quadrants(2);
        quadrants[^1].ModelId = "azure-foundry:gpt-5";
        quadrants[^1].ThreadId = "thread-1";

        MultiChatQuadrantRules.RemoveQuadrant(quadrants);

        Assert.Equal(2, quadrants.Count); // never drops below the floor
        Assert.Null(quadrants[^1].ModelId);
        Assert.Null(quadrants[^1].ThreadId);
    }
}
