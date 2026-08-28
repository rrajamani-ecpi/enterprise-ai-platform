using Bunit;
using EnterpriseAIPlatform.Application.Support;
using EnterpriseAIPlatform.Domain.Support;
using EnterpriseAIPlatform.Web.Components.Support;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EnterpriseAIPlatform.UnitTests.Web;

/// <summary>Spec 024 US5: newest-first render (as returned by IChangelogReader) and the defined empty state.</summary>
public class ChangelogViewComponentTests : BunitContext
{
    private readonly IChangelogReader _changelogReader = Substitute.For<IChangelogReader>();

    [Fact]
    public void ChangelogView_WithEntries_RendersNewestFirst()
    {
        _changelogReader.GetEntriesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ChangelogEntry>
            {
                new(new Version(1, 2, 0), "Second release"),
                new(new Version(1, 1, 0), "First release"),
            });
        Services.AddSingleton(_changelogReader);

        var cut = Render<ChangelogView>(builder =>
        {
            builder.OpenComponent<ChangelogView>(0);
            builder.CloseComponent();
        });
        cut.WaitForState(() => cut.Markup.Contains("1.2.0"));

        var firstIndex = cut.Markup.IndexOf("1.2.0", StringComparison.Ordinal);
        var secondIndex = cut.Markup.IndexOf("1.1.0", StringComparison.Ordinal);
        Assert.True(firstIndex >= 0 && secondIndex >= 0 && firstIndex < secondIndex);
    }

    [Fact]
    public void ChangelogView_NoEntries_RendersDefinedEmptyState()
    {
        _changelogReader.GetEntriesAsync(Arg.Any<CancellationToken>()).Returns(new List<ChangelogEntry>());
        Services.AddSingleton(_changelogReader);

        var cut = Render<ChangelogView>(builder =>
        {
            builder.OpenComponent<ChangelogView>(0);
            builder.CloseComponent();
        });
        cut.WaitForState(() => cut.Markup.Contains("changelog-empty"));

        Assert.Contains("changelog-empty", cut.Markup);
    }
}
