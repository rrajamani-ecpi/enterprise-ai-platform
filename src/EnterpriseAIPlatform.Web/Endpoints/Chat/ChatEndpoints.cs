using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Domain.Chat;

namespace EnterpriseAIPlatform.Web.Endpoints.Chat;

/// <summary>Spec 004's route surface — contracts/route-table.md.</summary>
public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chat/threads", async (
            CreateThreadRequest request,
            ICurrentUserAccessor currentUser,
            IIdentityHasher identityHasher,
            IChatThreadStore threadStore,
            CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var caller = callerResult.Response!;
            var partitionKey = identityHasher.ForEmail(caller.Email).Value;
            var thread = await threadStore.CreateAsync(partitionKey, caller.Email, request.ModelId, cancellationToken: ct);

            return Results.Ok(new ThreadResponse(thread.Id, thread.Version, thread.ModelId));
        });

        app.MapPost("/api/chat/threads/{id}/messages", async (
            string id,
            SendMessageRequest request,
            ICurrentUserAccessor currentUser,
            IChatPipeline pipeline,
            CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            ChatSendResult result;
            try
            {
                result = await pipeline.SendMessageAsync(callerResult.Response!, id, request.Text, request.ModelId, ct);
            }
            catch (Exception)
            {
                // FR-024: any unhandled exception in the chat entry point yields a generic 500,
                // never internal error detail.
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "An unexpected error occurred.");
            }

            return result switch
            {
                ChatSendResult.Rejected rejected => MapRejection(rejected),
                ChatSendResult.ContentBlocked blocked => Results.Json(
                    new { error = "CONTENT_BLOCKED", category = blocked.Category }, statusCode: StatusCodes.Status400BadRequest),
                ChatSendResult.Streaming streaming => StreamResult(streaming.Chunks, ct),
                _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "An unexpected error occurred."),
            };
        });

        return app;
    }

    private static IResult MapRejection(ChatSendResult.Rejected rejected) => rejected.Code switch
    {
        PreflightRejectionCode.ThreadReadOnly =>
            Results.Json(new { error = "THREAD_READ_ONLY" }, statusCode: StatusCodes.Status409Conflict),
        PreflightRejectionCode.MessageTooLong =>
            Results.Json(new { error = "MESSAGE_TOO_LONG" }, statusCode: StatusCodes.Status400BadRequest),
        PreflightRejectionCode.DailyLimitExceeded =>
            Results.Json(
                new { error = "DAILY_MESSAGE_LIMIT_EXCEEDED", resetsAt = rejected.ResetsAtUtc },
                statusCode: StatusCodes.Status402PaymentRequired),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "An unexpected error occurred."),
    };

    private static IResult StreamResult(IAsyncEnumerable<string> chunks, CancellationToken cancellationToken) =>
        Results.Stream(
            async stream =>
            {
                // AutoFlush triggers a synchronous Flush() internally, which TestServer (and some
                // hosts) disallow on the response body — flush explicitly and asynchronously instead.
                var writer = new StreamWriter(stream);
                await foreach (var chunk in chunks.WithCancellation(cancellationToken))
                {
                    await writer.WriteAsync($"data: {chunk}\n\n");
                    await writer.FlushAsync(cancellationToken);
                }

                await writer.WriteAsync("data: [DONE]\n\n");
                await writer.FlushAsync(cancellationToken);
            },
            "text/event-stream");

    public sealed record CreateThreadRequest(string ModelId);

    public sealed record ThreadResponse(string Id, string Version, string ModelId);

    public sealed record SendMessageRequest(string Text, string ModelId);
}
