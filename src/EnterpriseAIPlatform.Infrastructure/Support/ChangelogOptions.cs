namespace EnterpriseAIPlatform.Infrastructure.Support;

/// <summary>File-system changelog source configuration (spec 017 D1).</summary>
public sealed class ChangelogOptions
{
    public const string SectionName = "Changelog";

    public string ContentDirectory { get; set; } = "content/changelog";
}
