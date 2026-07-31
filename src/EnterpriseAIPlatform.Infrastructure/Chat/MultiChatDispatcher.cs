using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Domain.Chat;

namespace EnterpriseAIPlatform.Infrastructure.Chat;

/// <summary>
/// Fans a single message out to every assigned quadrant concurrently via spec 004's
/// <see cref="IChatPipeline"/>, merging each quadrant's independent stream into one tagged
/// sequence (spec 006 D4). On-demand thread creation (FR-003) happens here, persisted immediately
/// via <see cref="IMultiChatSessionStore"/> before that quadrant's send proceeds. A quadrant's
/// failure produces an <see cref="QuadrantEventKind.Error"/> event for that quadrant only and
/// never faults the shared channel or blocks another quadrant's writer task (FR-006/FR-011).
/// No <c>IMultiChatDispatcher</c> interface exists in R1 — there is exactly one orchestration
/// shape needed and no second implementation or substitution point yet (Principle IV, YAGNI).
/// </summary>
public sealed class MultiChatDispatcher
{
    private readonly IMultiChatSessionStore _sessionStore;
    private readonly IChatThreadStore _threadStore;
    private readonly IChatPipeline _chatPipeline;

    public MultiChatDispatcher(
        IMultiChatSessionStore sessionStore, IChatThreadStore threadStore, IChatPipeline chatPipeline)
    {
        _sessionStore = sessionStore;
        _threadStore = threadStore;
        _chatPipeline = chatPipeline;
    }

    /// <summary>
    /// <paramref name="session"/> is fetched by the caller (the endpoint) *before* any stream
    /// starts, so a session-fetch failure surfaces as a clean 500 rather than an exception
    /// mid-stream — this method itself is an async iterator, so anything awaited inside it only
    /// runs once enumeration begins, after headers have already committed to 200.
    /// </summary>
    public async IAsyncEnumerable<QuadrantEvent> DispatchAsync(
        UserModel caller,
        MultiChatSession session,
        string text,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var ownerPartitionKey = session.PartitionKey;
        var assignedQuadrants = session.Quadrants.Where(q => q.ModelId is not null).ToList();

        var channel = Channel.CreateUnbounded<QuadrantEvent>();

        var writerTasks = assignedQuadrants
            .Select(quadrant => RunQuadrantAsync(quadrant, caller, session, text, channel.Writer, cancellationToken))
            .ToList();

        _ = Task.WhenAll(writerTasks).ContinueWith(
            _ => channel.Writer.TryComplete(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return evt;
        }
    }

    private async Task RunQuadrantAsync(
        MultiChatQuadrant quadrant,
        UserModel caller,
        MultiChatSession session,
        string text,
        ChannelWriter<QuadrantEvent> writer,
        CancellationToken cancellationToken)
    {
        var ownerPartitionKey = session.PartitionKey;
        try
        {
            var threadId = quadrant.ThreadId;
            if (threadId is null)
            {
                // FR-003: create on demand and persist the association *before* sending, so an
                // immediate refresh doesn't lose it even if the send itself later fails.
                var thread = await _threadStore.CreateAsync(
                    ownerPartitionKey, caller.Email, quadrant.ModelId!, session.Id, quadrant.Position, cancellationToken);
                threadId = thread.Id;
                await _sessionStore.SetQuadrantThreadAsync(ownerPartitionKey, quadrant.Position, threadId, cancellationToken);
            }

            var result = await _chatPipeline.SendMessageAsync(caller, threadId, text, quadrant.ModelId!, cancellationToken);
            await WriteResultAsync(quadrant.Position, result, writer, cancellationToken);
        }
        catch (Exception)
        {
            // A single quadrant's failure (e.g. on-demand thread creation) never faults the shared
            // channel or the request as a whole (FR-006) — it's this quadrant's problem only.
            await writer.WriteAsync(QuadrantEvent.Error(quadrant.Position, "SEND_FAILED"), cancellationToken);
        }
        finally
        {
            await writer.WriteAsync(QuadrantEvent.Done(quadrant.Position), cancellationToken);
        }
    }

    private static async Task WriteResultAsync(
        int position, ChatSendResult result, ChannelWriter<QuadrantEvent> writer, CancellationToken cancellationToken)
    {
        switch (result)
        {
            case ChatSendResult.Streaming streaming:
                await foreach (var chunk in streaming.Chunks.WithCancellation(cancellationToken))
                {
                    await writer.WriteAsync(QuadrantEvent.Chunk(position, chunk), cancellationToken);
                }

                break;

            case ChatSendResult.Rejected rejected:
                await writer.WriteAsync(QuadrantEvent.Error(position, rejected.Code.ToString()), cancellationToken);
                break;

            case ChatSendResult.ContentBlocked blocked:
                await writer.WriteAsync(QuadrantEvent.Error(position, $"CONTENT_BLOCKED:{blocked.Category}"), cancellationToken);
                break;
        }
    }
}
