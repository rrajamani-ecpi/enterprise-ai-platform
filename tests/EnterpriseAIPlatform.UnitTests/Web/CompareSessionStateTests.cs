using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.Chat;
using EnterpriseAIPlatform.Domain.Identity;
using EnterpriseAIPlatform.Domain.ModelAccess;
using EnterpriseAIPlatform.Infrastructure.Chat;
using EnterpriseAIPlatform.Web.Services;
using NSubstitute;

namespace EnterpriseAIPlatform.UnitTests.Web;

/// <summary>
/// Spec 024 US4 (FR-008/FR-009): every dependency is faked, including a real
/// <see cref="MultiChatDispatcher"/> built from faked lower-level stores — it's a sealed class
/// with no interface (spec 006 D4, YAGNI), so it's exercised directly rather than substituted,
/// mirroring <c>MultiChatDispatcherTests</c>'s own approach.
/// </summary>
public class CompareSessionStateTests
{
    private const string PartitionKey = "hashed-owner";
    private const string ModelA = "azure-foundry:gpt-5";
    private const string ModelB = "azure-foundry:other";

    private readonly ICurrentUserAccessor _currentUserAccessor = Substitute.For<ICurrentUserAccessor>();
    private readonly IIdentityHasher _identityHasher = Substitute.For<IIdentityHasher>();
    private readonly IMultiChatSessionStore _sessionStore = Substitute.For<IMultiChatSessionStore>();
    private readonly IChatThreadStore _threadStore = Substitute.For<IChatThreadStore>();
    private readonly IChatPipeline _chatPipeline = Substitute.For<IChatPipeline>();
    private readonly IModelAccessService _modelAccessService = Substitute.For<IModelAccessService>();
    private readonly IChatMessageStore _messageStore = Substitute.For<IChatMessageStore>();
    private readonly UserModel _caller = new() { Name = "Alice", Email = "alice@contoso.com" };

    public CompareSessionStateTests()
    {
        _currentUserAccessor.GetCurrentUser().Returns(ServerActionResponse<UserModel>.Ok(_caller));
        _identityHasher.ForEmail(_caller.Email).Returns(new StoragePartitionKey(PartitionKey));
        _modelAccessService.GetAvailableModelsAsync(_caller, Arg.Any<CancellationToken>())
            .Returns(ServerActionResponse<IReadOnlyList<ModelConfigDocument>>.Ok(
                new List<ModelConfigDocument> { Model(ModelA), Model(ModelB) }));
        _messageStore.ListByThreadAsync(Arg.Any<string>(), PartitionKey, Arg.Any<CancellationToken>())
            .Returns(new List<ChatMessageModel>());
    }

    private CompareSessionState CreateState() => new(
        _currentUserAccessor,
        _identityHasher,
        _sessionStore,
        new MultiChatDispatcher(_sessionStore, _threadStore, _chatPipeline),
        _modelAccessService,
        _messageStore);

    private static ModelConfigDocument Model(string id) => new() { Id = id, DisplayName = id, Provider = "azure-foundry" };

    private static MultiChatSession Session(params MultiChatQuadrant[] quadrants) => new()
    {
        Id = "multichat:" + PartitionKey, PartitionKey = PartitionKey, OwnerUserId = "alice@contoso.com",
        Quadrants = quadrants.ToList(),
    };

    private static async IAsyncEnumerable<string> Chunks(params string[] values)
    {
        foreach (var value in values)
        {
            await Task.Yield();
            yield return value;
        }
    }

    [Fact]
    public async Task InitializeAsync_ProjectsPanesFromSession_AndLoadsAvailableModels()
    {
        var session = Session(new MultiChatQuadrant { Position = 0 }, new MultiChatQuadrant { Position = 1 });
        _sessionStore.GetOrCreateAsync(PartitionKey, _caller.Email, Arg.Any<CancellationToken>()).Returns(session);
        var state = CreateState();

        await state.InitializeAsync();

        Assert.Equal(2, state.Panes.Count);
        Assert.All(state.Panes, p => Assert.Null(p.ModelId));
        Assert.Equal(2, state.AvailableModels.Count);
    }

    [Fact]
    public async Task AddPaneAsync_BelowCap_AddsPane()
    {
        var session = Session(new MultiChatQuadrant { Position = 0 }, new MultiChatQuadrant { Position = 1 });
        _sessionStore.GetOrCreateAsync(PartitionKey, _caller.Email, Arg.Any<CancellationToken>()).Returns(session);
        var expanded = Session(
            new MultiChatQuadrant { Position = 0 }, new MultiChatQuadrant { Position = 1 }, new MultiChatQuadrant { Position = 2 });
        _sessionStore.AddQuadrantAsync(PartitionKey, Arg.Any<CancellationToken>())
            .Returns(ServerActionResponse<MultiChatSession>.Ok(expanded));
        var state = CreateState();
        await state.InitializeAsync();

        await state.AddPaneAsync();

        Assert.Equal(3, state.Panes.Count);
        Assert.Null(state.PaneErrorMessage);
    }

    [Fact]
    public async Task AddPaneAsync_AtCap_SetsPaneErrorMessage_LeavesPaneCountUnchanged()
    {
        var session = Session(Enumerable.Range(0, 4).Select(i => new MultiChatQuadrant { Position = i }).ToArray());
        _sessionStore.GetOrCreateAsync(PartitionKey, _caller.Email, Arg.Any<CancellationToken>()).Returns(session);
        _sessionStore.AddQuadrantAsync(PartitionKey, Arg.Any<CancellationToken>())
            .Returns(ServerActionResponse<MultiChatSession>.Error("A multi-chat session may not exceed 4 quadrants."));
        var state = CreateState();
        await state.InitializeAsync();

        await state.AddPaneAsync();

        Assert.Equal(4, state.Panes.Count);
        Assert.NotNull(state.PaneErrorMessage);
    }

    [Fact]
    public async Task RemovePaneAsync_AtFloor_ReflectsStoresClearedAssignment_PaneCountUnchanged()
    {
        var session = Session(
            new MultiChatQuadrant { Position = 0, ModelId = ModelA }, new MultiChatQuadrant { Position = 1 });
        _sessionStore.GetOrCreateAsync(PartitionKey, _caller.Email, Arg.Any<CancellationToken>()).Returns(session);
        var cleared = Session(new MultiChatQuadrant { Position = 0 }, new MultiChatQuadrant { Position = 1 });
        _sessionStore.RemoveQuadrantAsync(PartitionKey, Arg.Any<CancellationToken>()).Returns(cleared);
        var state = CreateState();
        await state.InitializeAsync();

        await state.RemovePaneAsync();

        Assert.Equal(2, state.Panes.Count);
        Assert.Null(state.Panes[0].ModelId);
    }

    [Fact]
    public async Task AssignModelAsync_UpdatesMatchingPane_LeavesOthersUnaffected()
    {
        var session = Session(new MultiChatQuadrant { Position = 0 }, new MultiChatQuadrant { Position = 1 });
        _sessionStore.GetOrCreateAsync(PartitionKey, _caller.Email, Arg.Any<CancellationToken>()).Returns(session);
        var assigned = Session(
            new MultiChatQuadrant { Position = 0, ModelId = ModelA }, new MultiChatQuadrant { Position = 1 });
        _sessionStore.AssignModelAsync(PartitionKey, 0, ModelA, Arg.Any<CancellationToken>()).Returns(assigned);
        var state = CreateState();
        await state.InitializeAsync();

        await state.AssignModelAsync(0, ModelA);

        Assert.Equal(ModelA, state.Panes[0].ModelId);
        Assert.Equal(ModelA, state.Panes[0].ModelDisplayName);
        Assert.Null(state.Panes[1].ModelId);
    }

    [Fact]
    public async Task SendToAllAsync_RoutesChunksToCorrectPane_OneErrorDoesNotAffectOthers()
    {
        var session = Session(
            new MultiChatQuadrant { Position = 0, ModelId = ModelA, ThreadId = "thread-0" },
            new MultiChatQuadrant { Position = 1, ModelId = ModelB, ThreadId = "thread-1" });
        _sessionStore.GetOrCreateAsync(PartitionKey, _caller.Email, Arg.Any<CancellationToken>()).Returns(session);
        _chatPipeline.SendMessageAsync(_caller, "thread-0", "hi", ModelA, Arg.Any<CancellationToken>())
            .Returns<Task<ChatSendResult>>(_ => throw new InvalidOperationException("boom"));
        _chatPipeline.SendMessageAsync(_caller, "thread-1", "hi", ModelB, Arg.Any<CancellationToken>())
            .Returns(new ChatSendResult.Streaming(Chunks("ok")));
        var state = CreateState();
        await state.InitializeAsync();
        state.ComposerText = "hi";

        await state.SendToAllAsync();

        Assert.False(state.IsSending);
        Assert.NotNull(state.Panes[0].ErrorMessage);
        var pane1Message = Assert.Single(state.Panes[1].Messages);
        Assert.Equal("ok", pane1Message.Content);
        Assert.True(pane1Message.IsComplete);
    }

    [Fact]
    public async Task SendToAllAsync_UnassignedPane_NeverDispatchedTo_MessagesStayEmpty()
    {
        var session = Session(
            new MultiChatQuadrant { Position = 0, ModelId = ModelA, ThreadId = "thread-0" },
            new MultiChatQuadrant { Position = 1 });
        _sessionStore.GetOrCreateAsync(PartitionKey, _caller.Email, Arg.Any<CancellationToken>()).Returns(session);
        _chatPipeline.SendMessageAsync(_caller, "thread-0", "hi", ModelA, Arg.Any<CancellationToken>())
            .Returns(new ChatSendResult.Streaming(Chunks("ok")));
        var state = CreateState();
        await state.InitializeAsync();
        state.ComposerText = "hi";

        await state.SendToAllAsync();

        Assert.Empty(state.Panes[1].Messages);
        Assert.Null(state.Panes[1].ErrorMessage);
    }
}
