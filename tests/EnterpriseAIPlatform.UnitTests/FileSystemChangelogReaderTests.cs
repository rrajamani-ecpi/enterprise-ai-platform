using EnterpriseAIPlatform.Infrastructure.Support;
using Microsoft.Extensions.Options;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>Spec 017 US1 / FR-001 / SC-001: the changelog reader never throws on a missing/malformed source.</summary>
public class FileSystemChangelogReaderTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "eap-changelog-tests-" + Guid.NewGuid());

    private FileSystemChangelogReader BuildReader(string directory) =>
        new(Options.Create(new ChangelogOptions { ContentDirectory = directory }));

    [Fact]
    public async Task GetEntriesAsync_MissingDirectory_ReturnsEmptyList_NeverThrows()
    {
        var reader = BuildReader(Path.Combine(_tempDirectory, "does-not-exist"));

        var entries = await reader.GetEntriesAsync();

        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetEntriesAsync_UnparseableFilename_IsExcluded_NotFatal()
    {
        Directory.CreateDirectory(_tempDirectory);
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "not-a-version.md"), "hello");
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "1.0.0.md"), "first release");

        var entries = await BuildReader(_tempDirectory).GetEntriesAsync();

        Assert.Single(entries);
        Assert.Equal(new Version(1, 0, 0), entries[0].Version);
    }

    [Fact]
    public async Task GetEntriesAsync_WellFormedDirectory_ReturnsEntriesNewestFirst()
    {
        Directory.CreateDirectory(_tempDirectory);
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "1.0.0.md"), "first");
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "1.2.0.md"), "second");
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "1.10.0.md"), "third"); // semantic, not lexicographic, order

        var entries = await BuildReader(_tempDirectory).GetEntriesAsync();

        Assert.Equal(3, entries.Count);
        Assert.Equal(new Version(1, 10, 0), entries[0].Version);
        Assert.Equal(new Version(1, 2, 0), entries[1].Version);
        Assert.Equal(new Version(1, 0, 0), entries[2].Version);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
