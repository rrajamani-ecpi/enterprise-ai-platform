using EnterpriseAIPlatform.Domain.Support;

namespace EnterpriseAIPlatform.Application.Support;

/// <summary>
/// The changelog content source (spec 017 FR-001–003). Never throws — an empty list signals a
/// missing or entirely-unparseable source, which every caller (the changelog page, version-alert)
/// treats as a safe, defined state rather than an error.
/// </summary>
public interface IChangelogReader
{
    /// <summary>Newest-first. Empty if the source is missing, empty, or unparseable.</summary>
    Task<IReadOnlyList<ChangelogEntry>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
