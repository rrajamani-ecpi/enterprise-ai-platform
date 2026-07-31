using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.Chat;
using EnterpriseAIPlatform.Infrastructure.Chat;

namespace EnterpriseAIPlatform.Web.Endpoints.MultiChat;

/// <summary>Spec 006's route surface — contracts/route-table.md.</summary>
public static class MultiChatEndpoints
{
    public static IEndpointRouteBuilder MapMultiChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/multichat/session", async (
            ICurrentUserAccessor currentUser, IIdentityHasher identityHasher, IMultiChatSessionStore sessionStore, CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var caller = callerResult.Response!;
            var partitionKey = identityHasher.ForEmail(caller.Email).Value;
            var session = await sessionStore.GetOrCreateAsync(partitionKey, caller.Email, ct);
            return Results.Ok(session);
        });

        app.MapPost("/api/multichat/session/quadrants", async (
            ICurrentUserAccessor currentUser, IIdentityHasher identityHasher, IMultiChatSessionStore sessionStore, CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var partitionKey = identityHasher.ForEmail(callerResult.Response!.Email).Value;
            await sessionStore.GetOrCreateAsync(partitionKey, callerResult.Response!.Email, ct);
            var result = await sessionStore.AddQuadrantAsync(partitionKey, ct);
            return result.Status == ResponseStatus.OK
                ? Results.Ok(result.Response)
                : Results.Json(new { error = "QUADRANT_CAP_EXCEEDED" }, statusCode: StatusCodes.Status400BadRequest);
        });

        app.MapDelete("/api/multichat/session/quadrants", async (
            ICurrentUserAccessor currentUser, IIdentityHasher identityHasher, IMultiChatSessionStore sessionStore, CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var partitionKey = identityHasher.ForEmail(callerResult.Response!.Email).Value;
            await sessionStore.GetOrCreateAsync(partitionKey, callerResult.Response!.Email, ct);
            var session = await sessionStore.RemoveQuadrantAsync(partitionKey, ct);
            return Results.Ok(session);
        });

        app.MapPut("/api/multichat/session/quadrants/{position:int}/model", async (
            int position,
            AssignModelRequest request,
            ICurrentUserAccessor currentUser,
            IIdentityHasher identityHasher,
            IMultiChatSessionStore sessionStore,
            IModelCatalogService modelCatalog,
            CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var modelResult = await modelCatalog.GetAsync(request.ModelId, cancellationToken: ct);
            if (modelResult.Status != ResponseStatus.OK || modelResult.Response is not { IsEnabled: true })
            {
                return Results.Json(new { error = "MODEL_NOT_FOUND_OR_DISABLED" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var caller = callerResult.Response!;
            var partitionKey = identityHasher.ForEmail(caller.Email).Value;
            await sessionStore.GetOrCreateAsync(partitionKey, caller.Email, ct);

            try
            {
                var session = await sessionStore.AssignModelAsync(partitionKey, position, request.ModelId, ct);
                return Results.Ok(session);
            }
            catch (InvalidOperationException)
            {
                return Results.Json(new { error = "QUADRANT_NOT_FOUND" }, statusCode: StatusCodes.Status404NotFound);
            }
        });

        app.MapPost("/api/multichat/session/messages", async (
            SendToAllRequest request,
            ICurrentUserAccessor currentUser,
            IIdentityHasher identityHasher,
            IMultiChatSessionStore sessionStore,
            MultiChatDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var caller = callerResult.Response!;
            var partitionKey = identityHasher.ForEmail(caller.Email).Value;

            MultiChatSession session;
            try
            {
                // Fetched here, before any stream starts, so a failure yields a clean 500 (FR-024
                // precedent from spec 004) rather than an exception mid-stream.
                session = await sessionStore.GetOrCreateAsync(partitionKey, caller.Email, ct);
            }
            catch (Exception)
            {
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "An unexpected error occurred.");
            }

            var events = dispatcher.DispatchAsync(caller, session, request.Text, ct);
            return StreamEvents(events, ct);
        });

        return app;
    }

    private static IResult StreamEvents(IAsyncEnumerable<QuadrantEvent> events, CancellationToken cancellationToken) =>
        Results.Stream(
            async stream =>
            {
                var writer = new StreamWriter(stream);
                await foreach (var evt in events.WithCancellation(cancellationToken))
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        position = evt.Position,
                        kind = evt.Kind.ToString().ToLowerInvariant(),
                        content = evt.Content,
                    });
                    await writer.WriteAsync($"data: {json}\n\n");
                    await writer.FlushAsync(cancellationToken);
                }
            },
            "text/event-stream");

    public sealed record AssignModelRequest(string ModelId);

    public sealed record SendToAllRequest(string Text);
}
