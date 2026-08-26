using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.Chat;
using EnterpriseAIPlatform.Domain.Identity;
using EnterpriseAIPlatform.Domain.ModelAccess;
using EnterpriseAIPlatform.Web.Services;
using NSubstitute;

namespace EnterpriseAIPlatform.UnitTests.Web;

/// <summary>
/// Spec 024 US2 (FR-002–FR-004, FR-012): every dependency is faked so the send/stream/error/
/// interruption flow is directly observable via NSubstitute, mirroring <c>ChatPipelineTests</c>'s style.
/// </summary>
public class ChatComposerStateTests
{
    private const string PartitionKey = "hashed-owner";
    private const string ThreadId = "thread-1";
    private const string FirstModelId = "azure-foundry:gpt-5";

    private readonly ICurrentUserAccessor _currentUserAccessor = Substitute.For<ICurrentUserAccessor>();
    private readonly IIdentityHasher _identityHasher = Substitute.For<IIdentityHasher>();
    private readonly IChatThreadStore _threadStore = Substitute.For<IChatThreadStore>();
    private readonly IChatMessageStore _messageStore = Substitute.For<IChatMessageStore>();
    private readonly IChatPipeline _chatPipeline = Substitute.For<IChatPipeline>();
    private readonly IModelAccessService _modelAccessService = Substitute.For<IModelAccessService>();
    private readonly UserModel _caller = new() { Name = "Alice", Email = "alice@contoso.com" };

    public ChatComposerStateTests()
    {
        _currentUserAccessor.GetCurrentUser().Returns(ServerActionResponse<UserModel>.Ok(_caller));
        _identityHasher.ForEmail(_caller.Email).Returns(new StoragePartitionKey(PartitionKey));
        _modelAccessService.GetAvailableModelsAsync(_caller, Arg.Any<CancellationToken>())
            .Returns(ServerActionResponse<IReadOnlyList<ModelConfigDocument>>.Ok(
                new List<ModelConfigDocument> { Model(FirstModelId), Model("azure-foundry:other") }));
        _threadStore.CreateAsync(PartitionKey, _caller.Email, FirstModelId, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new ChatThreadModel
            {
                Id = ThreadId, PartitionKey = PartitionKey, OwnerUserId = _caller.Email, ModelId = FirstModelId,
                DisplayName = "Conversation — Jan 1, 2026 12:00 PM",
            });
    }

    private ChatComposerState CreateState() =>
        new(_currentUserAccessor, _identityHasher, _threadStore, _messageStore, _chatPipeline, _modelAccessService);

    private static ModelConfigDocument Model(string id) => new() { Id = id, DisplayName = id, Provider = "azure-foundry" };

    private static async IAsyncEnumerable<string> ChunksOf(params string[] chunks)
    {
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return chunk;
        }
    }

    private static async IAsyncEnumerable<string> ChunksThatThrow()
    {
        await Task.Yield();
        yield return "Hi";
        throw new InvalidOperationException("connection dropped");
    }

    /// <summary>
    /// Chunks gated behind <paramref name="gate"/> so a test can deterministically observe state
    /// while streaming is genuinely in-flight, without racing a background continuation the way a
    /// bare <c>Task.Yield()</c>-based sequence would (its continuation can run on another thread
    /// pool thread before the test's own assertion executes).
    /// </summary>
    private static async IAsyncEnumerable<string> GatedChunksOf(TaskCompletionSource gate, params string[] chunks)
    {
        await gate.Task;
        foreach (var chunk in chunks)
        {
            yield return chunk;
        }
    }

    [Fact]
    public async Task SendAsync_FirstSend_CreatesThread_WithFirstEntitledModel()
    {
        _chatPipeline.SendMessageAsync(_caller, ThreadId, "hello", FirstModelId, Arg.Any<CancellationToken>())
            .Returns(new ChatSendResult.Streaming(ChunksOf("hi")));
        var state = CreateState();
        state.ComposerText = "hello";

        await state.SendAsync();

        await _threadStore.Received(1).CreateAsync(PartitionKey, _caller.Email, FirstModelId, cancellationToken: Arg.Any<CancellationToken>());
        Assert.Equal(ThreadId, state.ThreadId);
        Assert.Equal(FirstModelId, state.ModelId);
    }

    [Fact]
    public async Task SendAsync_SecondSend_ReusesThreadId_DoesNotCreateThreadAgain()
    {
        _chatPipeline.SendMessageAsync(_caller, ThreadId, Arg.Any<string>(), FirstModelId, Arg.Any<CancellationToken>())
            .Returns(_ => new ChatSendResult.Streaming(ChunksOf("hi")));
        var state = CreateState();
        state.ComposerText = "first";
        await state.SendAsync();

        state.ComposerText = "second";
        await state.SendAsync();

        await _threadStore.Received(1).CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>());
        Assert.Equal(ThreadId, state.ThreadId);
    }

    [Fact]
    public async Task SendAsync_WhileStreamStillInFlight_IsStreamingIsTrue_ThenFalseAfterCompletion()
    {
        var gate = new TaskCompletionSource();
        _chatPipeline.SendMessageAsync(_caller, ThreadId, "hello", FirstModelId, Arg.Any<CancellationToken>())
            .Returns(new ChatSendResult.Streaming(GatedChunksOf(gate, "Hi", " there")));
        var state = CreateState();
        state.ComposerText = "hello";

        var task = state.SendAsync();
        Assert.True(state.IsStreaming);

        gate.SetResult();
        await task;

        Assert.False(state.IsStreaming);
    }

    [Fact]
    public async Task SendAsync_Streaming_AppendsChunksIncrementally_AndCompletes()
    {
        _chatPipeline.SendMessageAsync(_caller, ThreadId, "hello", FirstModelId, Arg.Any<CancellationToken>())
            .Returns(new ChatSendResult.Streaming(ChunksOf("Hi", " there")));
        var state = CreateState();
        state.ComposerText = "hello";

        await state.SendAsync();

        Assert.False(state.IsStreaming);
        var assistantMessage = Assert.Single(state.Messages, m => m.Role == "assistant");
        Assert.Equal("Hi there", assistantMessage.Content);
        Assert.True(assistantMessage.IsComplete);
        Assert.False(assistantMessage.IsInterrupted);
        Assert.Equal(string.Empty, state.ComposerText);
    }

    [Fact]
    public async Task SendAsync_Rejected_SetsErrorMessage_AndRestoresComposerText()
    {
        _chatPipeline.SendMessageAsync(_caller, ThreadId, "hello", FirstModelId, Arg.Any<CancellationToken>())
            .Returns(new ChatSendResult.Rejected(PreflightRejectionCode.MessageTooLong));
        var state = CreateState();
        state.ComposerText = "hello";

        await state.SendAsync();

        Assert.False(state.IsStreaming);
        Assert.NotNull(state.ErrorMessage);
        Assert.Equal("hello", state.ComposerText);
        Assert.DoesNotContain(state.Messages, m => m.Role == "assistant");
    }

    [Fact]
    public async Task SendAsync_ContentBlocked_SetsErrorMessage_AndRestoresComposerText()
    {
        _chatPipeline.SendMessageAsync(_caller, ThreadId, "hello", FirstModelId, Arg.Any<CancellationToken>())
            .Returns(new ChatSendResult.ContentBlocked("hate"));
        var state = CreateState();
        state.ComposerText = "hello";

        await state.SendAsync();

        Assert.False(state.IsStreaming);
        Assert.Contains("hate", state.ErrorMessage);
        Assert.Equal("hello", state.ComposerText);
    }

    [Fact]
    public async Task SendAsync_UnhandledException_SetsGenericErrorMessage_NoInternalDetail()
    {
        _chatPipeline.SendMessageAsync(_caller, ThreadId, "hello", FirstModelId, Arg.Any<CancellationToken>())
            .Returns<ChatSendResult>(_ => throw new InvalidOperationException("Cosmos connection string missing"));
        var state = CreateState();
        state.ComposerText = "hello";

        await state.SendAsync();

        Assert.False(state.IsStreaming);
        Assert.NotNull(state.ErrorMessage);
        Assert.DoesNotContain("Cosmos", state.ErrorMessage);
    }

    [Fact]
    public async Task SendAsync_StreamInterrupted_MarksMessageInterrupted_KeepsPartialContentVisible()
    {
        _chatPipeline.SendMessageAsync(_caller, ThreadId, "hello", FirstModelId, Arg.Any<CancellationToken>())
            .Returns(new ChatSendResult.Streaming(ChunksThatThrow()));
        var state = CreateState();
        state.ComposerText = "hello";

        await state.SendAsync();

        Assert.False(state.IsStreaming);
        var assistantMessage = Assert.Single(state.Messages, m => m.Role == "assistant");
        Assert.Equal("Hi", assistantMessage.Content);
        Assert.True(assistantMessage.IsInterrupted);
        Assert.True(assistantMessage.IsComplete);
        Assert.NotNull(state.ErrorMessage);
    }

    [Fact]
    public async Task SwitchToAsync_ExistingOwnedThread_LoadsHistory()
    {
        var thread = new ChatThreadModel
        {
            Id = ThreadId, PartitionKey = PartitionKey, OwnerUserId = _caller.Email, ModelId = FirstModelId,
            DisplayName = "Trip planning",
        };
        _threadStore.GetAsync(ThreadId, PartitionKey, Arg.Any<CancellationToken>()).Returns(thread);
        _messageStore.ListByThreadAsync(ThreadId, PartitionKey, Arg.Any<CancellationToken>())
            .Returns(new List<ChatMessageModel>
            {
                new() { Id = "m1", PartitionKey = PartitionKey, ThreadId = ThreadId, Role = ChatMessageRole.User, Content = "hi", CreatedAtUtc = DateTimeOffset.UtcNow },
                new() { Id = "m2", PartitionKey = PartitionKey, ThreadId = ThreadId, Role = ChatMessageRole.Assistant, Content = "hello!", CreatedAtUtc = DateTimeOffset.UtcNow },
            });
        var state = CreateState();

        var found = await state.SwitchToAsync(ThreadId);

        Assert.False(state.IsNotFound);
        Assert.Equal(ThreadId, state.ThreadId);
        Assert.Equal(FirstModelId, state.ModelId);
        Assert.Equal(2, state.Messages.Count);
        Assert.Equal("hi", state.Messages[0].Content);
        Assert.Equal("user", state.Messages[0].Role);
        Assert.True(state.Messages[0].IsComplete);
        Assert.Equal("hello!", state.Messages[1].Content);
        Assert.Equal("assistant", state.Messages[1].Role);
    }

    [Fact]
    public async Task SwitchToAsync_ForeignOrNonexistentThread_SetsIsNotFound()
    {
        _threadStore.GetAsync("someone-elses-thread", PartitionKey, Arg.Any<CancellationToken>())
            .Returns((ChatThreadModel?)null);
        var state = CreateState();

        await state.SwitchToAsync("someone-elses-thread");

        Assert.True(state.IsNotFound);
        Assert.Null(state.ThreadId);
        Assert.Empty(state.Messages);
    }

    [Fact]
    public async Task SwitchToAsync_SuccessfulSwitch_ClearsIsNotFoundFromAPriorFailedSwitch()
    {
        _threadStore.GetAsync("bad-id", PartitionKey, Arg.Any<CancellationToken>()).Returns((ChatThreadModel?)null);
        var thread = new ChatThreadModel
        {
            Id = ThreadId, PartitionKey = PartitionKey, OwnerUserId = _caller.Email, ModelId = FirstModelId,
            DisplayName = "Trip planning",
        };
        _threadStore.GetAsync(ThreadId, PartitionKey, Arg.Any<CancellationToken>()).Returns(thread);
        _messageStore.ListByThreadAsync(ThreadId, PartitionKey, Arg.Any<CancellationToken>())
            .Returns(new List<ChatMessageModel>());
        var state = CreateState();
        await state.SwitchToAsync("bad-id");
        Assert.True(state.IsNotFound);

        await state.SwitchToAsync(ThreadId);

        Assert.False(state.IsNotFound);
        Assert.Equal(ThreadId, state.ThreadId);
    }

    [Fact]
    public async Task ResetToNew_ClearsActiveConversationState()
    {
        _chatPipeline.SendMessageAsync(_caller, ThreadId, "hello", FirstModelId, Arg.Any<CancellationToken>())
            .Returns(new ChatSendResult.Streaming(ChunksOf("hi")));
        var state = CreateState();
        state.ComposerText = "hello";
        await state.SendAsync();

        state.ResetToNew();

        Assert.Null(state.ThreadId);
        Assert.Null(state.ModelId);
        Assert.False(state.IsNotFound);
        Assert.Null(state.ErrorMessage);
        Assert.Equal(string.Empty, state.ComposerText);
        Assert.Empty(state.Messages);
    }
}
