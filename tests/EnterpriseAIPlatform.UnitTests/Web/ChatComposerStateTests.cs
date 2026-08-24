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
            });
    }

    private ChatComposerState CreateState() =>
        new(_currentUserAccessor, _identityHasher, _threadStore, _chatPipeline, _modelAccessService);

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
}
