using EnterpriseAIPlatform.Application.Support;
using EnterpriseAIPlatform.Domain.Support;
using Microsoft.Extensions.Options;

namespace EnterpriseAIPlatform.Infrastructure.Support;

/// <summary>
/// The single implementation of <see cref="IChangelogReader"/> (spec 017 FR-001–003, D1/D2). One
/// Markdown file per version; filename (without extension) is parsed as <see cref="Version"/>.
/// Never throws — a missing directory or an unparseable file is excluded, not fatal.
/// </summary>
public sealed class FileSystemChangelogReader : IChangelogReader
{
    private readonly ChangelogOptions _options;

    public FileSystemChangelogReader(IOptions<ChangelogOptions> options) => _options = options.Value;

    public Task<IReadOnlyList<ChangelogEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_options.ContentDirectory))
        {
            return Task.FromResult<IReadOnlyList<ChangelogEntry>>(Array.Empty<ChangelogEntry>());
        }

        var entries = new List<ChangelogEntry>();
        foreach (var file in Directory.EnumerateFiles(_options.ContentDirectory, "*.md"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (!Version.TryParse(fileName, out var version))
            {
                continue; // malformed filename — excluded, not fatal (FR-001)
            }

            string content;
            try
            {
                content = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue; // unreadable file — excluded, not fatal (FR-001)
            }

            entries.Add(new ChangelogEntry(version, content));
        }

        entries.Sort((a, b) => b.Version.CompareTo(a.Version)); // newest first
        return Task.FromResult<IReadOnlyList<ChangelogEntry>>(entries);
    }
}
