using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Domain.Chat;
using EnterpriseAIPlatform.Domain.Identity;
using EnterpriseAIPlatform.Web.Services;
using NSubstitute;

namespace EnterpriseAIPlatform.UnitTests.Web;

/// <summary>Spec 024 US3: list load and inline-rename flow, driven entirely through NSubstitute fakes.</summary>
public class ConversationListStateTests
{
    private const string PartitionKey = "hashed-owner";

    private readonly ICurrentUserAccessor _currentUserAccessor = Substitute.For<ICurrentUserAccessor>();
    private readonly IIdentityHasher _identityHasher = Substitute.For<IIdentityHasher>();
    private readonly IChatThreadStore _threadStore = Substitute.For<IChatThreadStore>();
    private readonly UserModel _caller = new() { Name = "Alice", Email = "alice@contoso.com" };

    public ConversationListStateTests()
    {
        _currentUserAccessor.GetCurrentUser().Returns(ServerActionResponse<UserModel>.Ok(_caller));
        _identityHasher.ForEmail(_caller.Email).Returns(new StoragePartitionKey(PartitionKey));
    }

    private ConversationListState CreateState() => new(_currentUserAccessor, _identityHasher, _threadStore);

    private static ChatThreadModel Thread(string id, string displayName) => new()
    {
        Id = id, PartitionKey = PartitionKey, OwnerUserId = "alice@contoso.com", ModelId = "azure-foundry:gpt-5",
        DisplayName = displayName,
    };

    [Fact]
    public async Task LoadConversationsAsync_PopulatesConversations()
    {
        _threadStore.ListByOwnerAsync(PartitionKey, Arg.Any<CancellationToken>())
            .Returns(new List<ChatThreadModel> { Thread("t1", "Trip planning"), Thread("t2", "Recipe ideas") });
        var state = CreateState();

        await state.LoadConversationsAsync();

        Assert.False(state.IsLoading);
        Assert.Equal(2, state.Conversations.Count);
        Assert.Contains(state.Conversations, c => c.Id == "t1" && c.DisplayName == "Trip planning");
    }

    [Fact]
    public async Task RenameAsync_Success_UpdatesTheMatchingEntry()
    {
        _threadStore.ListByOwnerAsync(PartitionKey, Arg.Any<CancellationToken>())
            .Returns(new List<ChatThreadModel> { Thread("t1", "Untitled") });
        var renamed = Thread("t1", "Trip planning");
        _threadStore.RenameAsync("t1", PartitionKey, "Trip planning", Arg.Any<CancellationToken>())
            .Returns(ServerActionResponse<ChatThreadModel>.Ok(renamed));
        var state = CreateState();
        await state.LoadConversationsAsync();

        await state.RenameAsync("t1", "Trip planning");

        Assert.Null(state.RenameErrorMessage);
        Assert.Equal("Trip planning", state.Conversations.Single(c => c.Id == "t1").DisplayName);
    }

    [Fact]
    public async Task LoadConversationsAsync_StoreThrows_SetsLoadErrorMessage_DoesNotPropagate()
    {
        _threadStore.ListByOwnerAsync(PartitionKey, Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<ChatThreadModel>>>(_ => throw new InvalidOperationException("Cosmos:AccountEndpoint is not configured"));
        var state = CreateState();

        await state.LoadConversationsAsync();

        Assert.False(state.IsLoading);
        Assert.NotNull(state.LoadErrorMessage);
        Assert.Empty(state.Conversations);
    }

    [Fact]
    public async Task RenameAsync_EmptyOrWhitespace_SetsRenameErrorMessage_LeavesEntryUnchanged()
    {
        _threadStore.ListByOwnerAsync(PartitionKey, Arg.Any<CancellationToken>())
            .Returns(new List<ChatThreadModel> { Thread("t1", "Untitled") });
        _threadStore.RenameAsync("t1", PartitionKey, "   ", Arg.Any<CancellationToken>())
            .Returns(ServerActionResponse<ChatThreadModel>.Error("A conversation name cannot be empty."));
        var state = CreateState();
        await state.LoadConversationsAsync();

        await state.RenameAsync("t1", "   ");

        Assert.NotNull(state.RenameErrorMessage);
        Assert.Equal("Untitled", state.Conversations.Single(c => c.Id == "t1").DisplayName);
    }
}
