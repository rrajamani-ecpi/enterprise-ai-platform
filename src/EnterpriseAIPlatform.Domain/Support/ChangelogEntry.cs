namespace EnterpriseAIPlatform.Domain.Support;

/// <summary>
/// A versioned changelog record (spec 017 Key Entities), sourced from file-system content — never
/// a database row. <see cref="IChangelogReader"/>-equivalent readers return an empty list rather
/// than throwing when the source is missing or malformed (FR-001).
/// </summary>
public sealed record ChangelogEntry(Version Version, string Content);
