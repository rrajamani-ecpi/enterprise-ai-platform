using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.Prompts;
using EnterpriseAIPlatform.Domain.Prompts;
using EnterpriseAIPlatform.Domain.Sharing;
using EnterpriseAIPlatform.Infrastructure.Prompts;

namespace EnterpriseAIPlatform.Web.Endpoints.Prompts;

/// <summary>Spec 016's route surface — contracts/route-table.md.</summary>
public static class PromptEndpoints
{
    public static IEndpointRouteBuilder MapPromptEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/prompts", async (
            ICurrentUserAccessor currentUser, IPromptService prompts, CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            // The service already applies the read filter server-side, so this returns only what
            // the caller is entitled to see (FR-001/FR-002).
            var result = await prompts.ListAsync(callerResult.Response!, ct);
            return Results.Ok(result.Response!.Select(p => new PromptListItemResponse(ToResponse(p.Prompt), p.IsFavorite)));
        });

        app.MapGet("/api/prompts/{id}", async (
            string id, ICurrentUserAccessor currentUser, IPromptService prompts, CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var result = await prompts.GetAsync(id, callerResult.Response!, ct);
            return ToHttpResult(result, ok => Results.Ok(ToResponse(ok)));
        });

        app.MapPost("/api/prompts", async (
            PromptWriteRequest request,
            ICurrentUserAccessor currentUser,
            IIdentityHasher identityHasher,
            IPromptService prompts,
            CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var draft = ToDraft(request, identityHasher);
            var result = await prompts.CreateAsync(draft, callerResult.Response!, ct);
            return ToHttpResult(result, ok => Results.Ok(ToResponse(ok)));
        });

        app.MapPatch("/api/prompts/{id}", async (
            string id,
            PromptWriteRequest request,
            ICurrentUserAccessor currentUser,
            IIdentityHasher identityHasher,
            IPromptService prompts,
            CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var draft = ToDraft(request, identityHasher);
            var result = await prompts.UpdateAsync(id, draft, callerResult.Response!, ct);
            return ToHttpResult(result, ok => Results.Ok(ToResponse(ok)));
        });

        app.MapDelete("/api/prompts/{id}", async (
            string id, ICurrentUserAccessor currentUser, IPromptService prompts, CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var result = await prompts.DeleteAsync(id, callerResult.Response!, ct);
            return ToHttpResult(result, _ => Results.Ok());
        });

        // Deliberately NOT RequireAdmin (unlike spec 009's persona transfer route): FR-006 grants
        // transfer to the owner OR an admin, and a route-level admin policy would reject the owner.
        // The owner-or-admin gate lives in PromptService via PromptAccessEvaluator.CanTransfer.
        app.MapPost("/api/prompts/{id}/transfer-ownership", async (
            string id,
            TransferPromptOwnershipRequest request,
            ICurrentUserAccessor currentUser,
            IPromptService prompts,
            CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var result = await prompts.TransferOwnershipAsync(id, request.NewOwnerEmail, callerResult.Response!, ct);
            return ToHttpResult(result, ok => Results.Ok(ToResponse(ok)));
        });

        app.MapGet("/api/prompts/favorites", async (
            ICurrentUserAccessor currentUser, IPromptService prompts, CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var result = await prompts.ListFavoritesAsync(callerResult.Response!, ct);
            return Results.Ok(result.Response!.Select(ToResponse));
        });

        app.MapPost("/api/prompts/{id}/favorite", async (
            string id, ICurrentUserAccessor currentUser, IPromptService prompts, CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var result = await prompts.AddFavoriteAsync(id, callerResult.Response!, ct);
            return ToHttpResult(result, _ => Results.Ok());
        });

        app.MapDelete("/api/prompts/{id}/favorite", async (
            string id, ICurrentUserAccessor currentUser, IPromptService prompts, CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var result = await prompts.RemoveFavoriteAsync(id, callerResult.Response!, ct);
            return ToHttpResult(result, _ => Results.Ok());
        });

        return app;
    }

    /// <summary>
    /// Maps a <see cref="ServerActionResponse{T}"/> to the HTTP shape per
    /// contracts/authorization-policies.md. Deliberately has <b>no</b> <c>NOT_FOUND</c> branch,
    /// unlike spec 009's equivalent: 404 and 401 are distinguishable by a caller, so routing a
    /// missing prompt to 404 would reintroduce exactly the enumeration oracle FR-009 forbids —
    /// PromptService never returns NOT_FOUND, and this mapper structurally cannot express it.
    /// </summary>
    private static IResult ToHttpResult<T>(ServerActionResponse<T> result, Func<T, IResult> onSuccess) => result.Status switch
    {
        ResponseStatus.OK => onSuccess(result.Response!),
        ResponseStatus.UNAUTHORIZED => Results.Json(result, statusCode: StatusCodes.Status401Unauthorized),
        ResponseStatus.ERROR when result.Errors.Count > 0 && result.Errors[0].Message == PromptService.ConcurrencyConflictMessage =>
            Results.Json(result, statusCode: StatusCodes.Status409Conflict),
        _ => Results.BadRequest(result.Errors),
    };

    private static PromptModel ToDraft(PromptWriteRequest request, IIdentityHasher identityHasher) => new()
    {
        // Owner fields are placeholders here and are always overwritten server-side by
        // PromptService from the authenticated caller — never from this request body.
        Id = string.Empty,
        OwnerUserId = string.Empty,
        OwnerPartitionKey = string.Empty,
        Name = request.Name,
        Description = request.Description,
        CollaboratorPartitionKeys = request.CollaboratorEmails.Select(e => identityHasher.ForEmail(e).Value).ToList(),
        SharedWith = request.SharedWith.Select(t => t.Type == "Group"
            ? PromptShareTarget.ForGroup(t.GroupToken!)
            : PromptShareTarget.ForIndividual(identityHasher.ForEmail(t.Identity!).Value)).ToList(),
    };

    private static PromptResponse ToResponse(PromptPublicDTO dto) => new(
        dto.Id, dto.OwnerUserId, dto.Name, dto.Description,
        dto.CollaboratorPartitionKeys, dto.SharedWith, dto.CreatedAtUtc, dto.UpdatedAtUtc);

    public sealed record PromptWriteRequest(
        string Name,
        string Description,
        List<string> CollaboratorEmails,
        List<PromptShareTargetRequest> SharedWith);

    /// <summary><paramref name="Type"/> is <c>"Individual"</c> (with <paramref name="Identity"/> set) or <c>"Group"</c> (with <paramref name="GroupToken"/> set).</summary>
    public sealed record PromptShareTargetRequest(string Type, string? Identity, string? GroupToken);

    public sealed record PromptResponse(
        string Id, string OwnerUserId, string Name, string Description,
        IReadOnlyList<string> CollaboratorPartitionKeys, IReadOnlyList<PromptShareTarget> SharedWith,
        DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

    public sealed record PromptListItemResponse(PromptResponse Prompt, bool IsFavorite);
}
