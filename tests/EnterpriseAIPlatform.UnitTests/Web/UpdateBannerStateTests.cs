using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.Support;
using EnterpriseAIPlatform.Domain.Identity;
using EnterpriseAIPlatform.Domain.Support;
using EnterpriseAIPlatform.Web.Services;
using NSubstitute;

namespace EnterpriseAIPlatform.UnitTests.Web;

/// <summary>Spec 024 US5 (FR-011): show/dismiss and the revert-on-persist-failure guarantee (Constitution Principle III).</summary>
public class UpdateBannerStateTests
{
    private const string PartitionKey = "hashed-owner";

    private readonly ICurrentUserAccessor _currentUserAccessor = Substitute.For<ICurrentUserAccessor>();
    private readonly IIdentityHasher _identityHasher = Substitute.For<IIdentityHasher>();
    private readonly IChangelogReader _changelogReader = Substitute.For<IChangelogReader>();
    private readonly IVersionAcknowledgmentStore _acknowledgmentStore = Substitute.For<IVersionAcknowledgmentStore>();
    private readonly UserModel _caller = new() { Name = "Alice", Email = "alice@contoso.com" };

    public UpdateBannerStateTests()
    {
        _currentUserAccessor.GetCurrentUser().Returns(ServerActionResponse<UserModel>.Ok(_caller));
        _identityHasher.ForEmail(_caller.Email).Returns(new StoragePartitionKey(PartitionKey));
    }

    private UpdateBannerState CreateState() =>
        new(_currentUserAccessor, _identityHasher, _changelogReader, _acknowledgmentStore);

    [Fact]
    public async Task InitializeAsync_NoPriorAcknowledgment_ShowsAlert()
    {
        _changelogReader.GetEntriesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ChangelogEntry> { new(new Version(1, 1, 0), "content") });
        _acknowledgmentStore.GetAsync(PartitionKey, Arg.Any<CancellationToken>()).Returns((VersionAcknowledgmentModel?)null);
        var state = CreateState();

        await state.InitializeAsync();

        Assert.True(state.ShowAlert);
        Assert.Equal("1.1.0", state.LatestVersion);
    }

    [Fact]
    public async Task InitializeAsync_RecentAcknowledgment_DoesNotShowAlert()
    {
        _changelogReader.GetEntriesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ChangelogEntry> { new(new Version(1, 1, 0), "content") });
        _acknowledgmentStore.GetAsync(PartitionKey, Arg.Any<CancellationToken>())
            .Returns(new VersionAcknowledgmentModel
            {
                Id = PartitionKey,
                PartitionKey = PartitionKey,
                AcknowledgedVersion = "1.1.0",
                AcknowledgedAtUtc = DateTimeOffset.UtcNow,
            });
        var state = CreateState();

        await state.InitializeAsync();

        Assert.False(state.ShowAlert);
    }

    [Fact]
    public async Task InitializeAsync_StoreThrows_FailsOpen_DoesNotPropagate()
    {
        _changelogReader.GetEntriesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<ChangelogEntry>>>(_ => throw new InvalidOperationException("Cosmos unavailable"));
        var state = CreateState();

        await state.InitializeAsync();

        Assert.False(state.ShowAlert);
    }

    [Fact]
    public async Task DismissAsync_SetsIsDismissed_AndPersistsAcknowledgment()
    {
        _changelogReader.GetEntriesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ChangelogEntry> { new(new Version(1, 1, 0), "content") });
        _acknowledgmentStore.GetAsync(PartitionKey, Arg.Any<CancellationToken>()).Returns((VersionAcknowledgmentModel?)null);
        var state = CreateState();
        await state.InitializeAsync();

        await state.DismissAsync();

        Assert.True(state.IsDismissed);
        await _acknowledgmentStore.Received(1).SetAsync(PartitionKey, "1.1.0", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DismissAsync_PersistFails_RevertsIsDismissed()
    {
        _changelogReader.GetEntriesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ChangelogEntry> { new(new Version(1, 1, 0), "content") });
        _acknowledgmentStore.GetAsync(PartitionKey, Arg.Any<CancellationToken>()).Returns((VersionAcknowledgmentModel?)null);
        _acknowledgmentStore.SetAsync(PartitionKey, "1.1.0", Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("Cosmos unavailable"));
        var state = CreateState();
        await state.InitializeAsync();

        await state.DismissAsync();

        Assert.False(state.IsDismissed);
    }
}
