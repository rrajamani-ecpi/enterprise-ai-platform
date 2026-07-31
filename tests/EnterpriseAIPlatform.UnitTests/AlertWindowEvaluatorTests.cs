using EnterpriseAIPlatform.Application.Support;
using EnterpriseAIPlatform.Domain.Support;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>Spec 017 US4 / FR-011 / SC-005: the 60-day acknowledgment cooldown window.</summary>
public class AlertWindowEvaluatorTests
{
    private static readonly ChangelogEntry Latest = new(new Version(2, 0, 0), "content");
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShouldShowAlert_NoLatestVersion_ReturnsFalse()
    {
        Assert.False(AlertWindowEvaluator.ShouldShowAlert(null, null, Now));
    }

    [Fact]
    public void ShouldShowAlert_NoAcknowledgment_ReturnsTrue()
    {
        Assert.True(AlertWindowEvaluator.ShouldShowAlert(Latest, null, Now));
    }

    [Fact]
    public void ShouldShowAlert_AcknowledgedJustUnder60DaysAgo_ReturnsFalse()
    {
        var ack = Acknowledgment(Now.AddDays(-59));

        Assert.False(AlertWindowEvaluator.ShouldShowAlert(Latest, ack, Now));
    }

    [Fact]
    public void ShouldShowAlert_AcknowledgedJustOver60DaysAgo_ReturnsTrue()
    {
        var ack = Acknowledgment(Now.AddDays(-61));

        Assert.True(AlertWindowEvaluator.ShouldShowAlert(Latest, ack, Now));
    }

    private static VersionAcknowledgmentModel Acknowledgment(DateTimeOffset acknowledgedAtUtc) => new()
    {
        Id = "test", PartitionKey = "test", AcknowledgedVersion = "1.0.0", AcknowledgedAtUtc = acknowledgedAtUtc,
    };
}
