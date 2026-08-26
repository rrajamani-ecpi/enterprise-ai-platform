using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.Chat;
using EnterpriseAIPlatform.Domain.ModelAccess;
using EnterpriseAIPlatform.Infrastructure.Chat;
using NSubstitute;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>
/// Spec 006 D4/D6, FR-006/FR-010/FR-011: the fan-out/fan-in merge dispatches every assigned
/// quadrant concurrently and isolates one quadrant's failure from the others.
/// </summary>
public class MultiChatDispatcherTests
{
    private const string PartitionKey = "hashed-owner";

    private readonly IMultiChatSessionStore _sessionStore = Substitute.For<IMultiChatSessionStore>();
    private readonly IChatThreadStore _threadStore = Substitute.For<IChatThreadStore>();
    private readonly IChatPipeline _chatPipeline = Substitute.For<IChatPipeline>();
    private readonly UserModel _caller = new() { Name = "Alice", Email = "alice@contoso.com" };

    private MultiChatDispatcher BuildDispatcher() => new(_sessionStore, _threadStore, _chatPipeline);

    private static MultiChatSession Session(params MultiChatQuadrant[] quadrants) => new()
    {
        Id = "multichat:" + PartitionKey,
        PartitionKey = PartitionKey,
        OwnerUserId = "alice@contoso.com",
        Quadrants = quadrants.ToList(),
    };

    private static ModelConfigDocument Model(string id) => new()
    {
        Id = id, DisplayName = "Test", Provider = "azure-foundry", IsEnabled = true,
        SupportsToolCalling = true, SupportsVision = true, SupportsReasoning = true,
        AccessTier = ModelAccessTier.Standard,
    };

    private static async IAsyncEnumerable<string> Chunks(params string[] values)
    {
        foreach (var v in values)
        {
            await Task.Yield();
            yield return v;
        }
    }

    [Fact]
    public async Task DispatchAsync_MergesChunksFromMultipleQuadrants_EachTaggedWithItsPosition()
    {
        var session = Session(
            new MultiChatQuadrant { Position = 0, ModelId = "m:a", ThreadId = "thread-0" },
            new MultiChatQuadrant { Position = 1, ModelId = "m:b", ThreadId = "thread-1" });

        _chatPipeline.SendMessageAsync(_caller, "thread-0", "hi", "m:a", Arg.Any<CancellationToken>())
            .Returns(new ChatSendResult.Streaming(Chunks("a1", "a2")));
        _chatPipeline.SendMessageAsync(_caller, "thread-1", "hi", "m:b", Arg.Any<CancellationToken>())
            .Returns(new ChatSendResult.Streaming(Chunks("b1")));

        var events = await CollectAsync(BuildDispatcher().DispatchAsync(_caller, session, "hi"));

        Assert.Contains(events, e => e.Position == 0 && e.Kind == QuadrantEventKind.Chunk && e.Content == "a1");
        Assert.Contains(events, e => e.Position == 0 && e.Kind == QuadrantEventKind.Chunk && e.Content == "a2");
        Assert.Contains(events, e => e.Position == 1 && e.Kind == QuadrantEventKind.Chunk && e.Content == "b1");
        Assert.Contains(events, e => e.Position == 0 && e.Kind == QuadrantEventKind.Done);
        Assert.Contains(events, e => e.Position == 1 && e.Kind == QuadrantEventKind.Done);
    }

    [Fact]
    public async Task DispatchAsync_OneQuadrantThrows_OthersStillCompleteNormally()
    {
        var session = Session(
            new MultiChatQuadrant { Position = 0, ModelId = "m:a", ThreadId = "thread-0" },
            new MultiChatQuadrant { Position = 1, ModelId = "m:b", ThreadId = "thread-1" });

        _chatPipeline.SendMessageAsync(_caller, "thread-0", "hi", "m:a", Arg.Any<CancellationToken>())
            .Returns<Task<ChatSendResult>>(_ => throw new InvalidOperationException("boom"));
        _chatPipeline.SendMessageAsync(_caller, "thread-1", "hi", "m:b", Arg.Any<CancellationToken>())
            .Returns(new ChatSendResult.Streaming(Chunks("ok")));

        var events = await CollectAsync(BuildDispatcher().DispatchAsync(_caller, session, "hi"));

        Assert.Contains(events, e => e.Position == 0 && e.Kind == QuadrantEventKind.Error);
        Assert.Contains(events, e => e.Position == 1 && e.Kind == QuadrantEventKind.Chunk && e.Content == "ok");
        Assert.Contains(events, e => e.Position == 1 && e.Kind == QuadrantEventKind.Done);
    }

    [Fact]
    public async Task DispatchAsync_EmptyThreadId_CreatesOnDemand_AndPersistsBeforeSending()
    {
        var session = Session(new MultiChatQuadrant { Position = 0, ModelId = "m:a", ThreadId = null });

        _threadStore.CreateAsync(PartitionKey, _caller.Email, "m:a", session.Id, 0, Arg.Any<CancellationToken>())
            .Returns(new ChatThreadModel
            {
                Id = "new-thread", PartitionKey = PartitionKey, OwnerUserId = _caller.Email, ModelId = "m:a",
                DisplayName = "Conversation — Jan 1, 2026 12:00 PM",
                MultiChatSessionId = session.Id, MultiChatPosition = 0,
            });
        _chatPipeline.SendMessageAsync(_caller, "new-thread", "hi", "m:a", Arg.Any<CancellationToken>())
            .Returns(new ChatSendResult.Streaming(Chunks("ok")));

        await CollectAsync(BuildDispatcher().DispatchAsync(_caller, session, "hi"));

        await _sessionStore.Received().SetQuadrantThreadAsync(PartitionKey, 0, "new-thread", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_UnassignedQuadrant_IsSkipped_NoEventsProduced()
    {
        var session = Session(
            new MultiChatQuadrant { Position = 0, ModelId = null },
            new MultiChatQuadrant { Position = 1, ModelId = "m:b", ThreadId = "thread-1" });

        _chatPipeline.SendMessageAsync(_caller, "thread-1", "hi", "m:b", Arg.Any<CancellationToken>())
            .Returns(new ChatSendResult.Streaming(Chunks("ok")));

        var events = await CollectAsync(BuildDispatcher().DispatchAsync(_caller, session, "hi"));

        Assert.DoesNotContain(events, e => e.Position == 0);
    }

    private static async Task<List<QuadrantEvent>> CollectAsync(IAsyncEnumerable<QuadrantEvent> source)
    {
        var list = new List<QuadrantEvent>();
        await foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }
}
