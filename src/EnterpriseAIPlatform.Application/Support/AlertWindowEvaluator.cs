using EnterpriseAIPlatform.Domain.Support;

namespace EnterpriseAIPlatform.Application.Support;

/// <summary>
/// The single implementation of the version-alert visibility window (spec 017 FR-011, D3) —
/// framework-free so it's unit-testable without a database, mirroring spec 014's
/// <c>ModelAccessEvaluator</c> and spec 006's <c>MultiChatQuadrantRules</c> pattern.
/// </summary>
public static class AlertWindowEvaluator
{
    private static readonly TimeSpan CooldownWindow = TimeSpan.FromDays(60);

    /// <summary>
    /// The 60-day window is a cooldown since the *last* acknowledgment (of any version), not a
    /// per-version "have you seen this one" flag — see research.md D3.
    /// </summary>
    public static bool ShouldShowAlert(ChangelogEntry? latest, VersionAcknowledgmentModel? acknowledgment, DateTimeOffset now)
    {
        if (latest is null)
        {
            return false;
        }

        return acknowledgment is null || now - acknowledgment.AcknowledgedAtUtc > CooldownWindow;
    }
}
