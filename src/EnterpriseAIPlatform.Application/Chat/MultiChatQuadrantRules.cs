using EnterpriseAIPlatform.Domain.Chat;

namespace EnterpriseAIPlatform.Application.Chat;

/// <summary>
/// The single implementation of the quadrant floor/cap invariant (spec 006 FR-004/005, D2) —
/// framework-free so it's unit-testable without a database, mirroring spec 014's
/// <c>ModelAccessEvaluator</c> pattern (Constitution Principle IV).
/// </summary>
public static class MultiChatQuadrantRules
{
    public const int MinQuadrants = 2;
    public const int MaxQuadrants = 4;

    /// <summary>Returns false (no mutation) if already at the 4-quadrant cap (FR-005).</summary>
    public static bool TryAddQuadrant(List<MultiChatQuadrant> quadrants)
    {
        if (quadrants.Count >= MaxQuadrants)
        {
            return false;
        }

        quadrants.Add(new MultiChatQuadrant { Position = quadrants.Count });
        return true;
    }

    /// <summary>
    /// Above the 2-quadrant floor, removes the highest-position quadrant. At the floor, clears
    /// that quadrant's assignment instead of removing the slot (FR-004).
    /// </summary>
    public static void RemoveQuadrant(List<MultiChatQuadrant> quadrants)
    {
        var last = quadrants[^1];

        if (quadrants.Count > MinQuadrants)
        {
            quadrants.RemoveAt(quadrants.Count - 1);
        }
        else
        {
            last.PersonaId = null;
            last.ModelId = null;
            last.ThreadId = null;
        }
    }
}
