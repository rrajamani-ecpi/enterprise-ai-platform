using EnterpriseAIPlatform.Application.Authorization;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.Personas;
using EnterpriseAIPlatform.Domain.Personas;
using EnterpriseAIPlatform.Infrastructure.Personas;

namespace EnterpriseAIPlatform.Web.Endpoints.Personas;

/// <summary>Spec 009's route surface — contracts/route-table.md.</summary>
public static class PersonaEndpoints
{
    public static IEndpointRouteBuilder MapPersonaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/personas", async (
            ICurrentUserAccessor currentUser, IPersonaService personas, CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var result = await personas.ListAsync(callerResult.Response!, ct);
            return Results.Ok(result.Response!.Select(ToResponse));
        });

        app.MapGet("/api/personas/{id}", async (
            string id, ICurrentUserAccessor currentUser, IPersonaService personas, CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var result = await personas.GetAsync(id, callerResult.Response!, ct);
            return ToHttpResult(result, ok => Results.Ok(ToResponse(ok)));
        });

        app.MapPost("/api/personas", async (
            PersonaWriteRequest request,
            ICurrentUserAccessor currentUser,
            IIdentityHasher identityHasher,
            IPersonaService personas,
            CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var draft = ToDraft(request, identityHasher);
            var result = await personas.CreateAsync(draft, callerResult.Response!, ct);
            return ToHttpResult(result, ok => Results.Ok(ToResponse(ok)));
        });

        app.MapPatch("/api/personas/{id}", async (
            string id,
            PersonaWriteRequest request,
            ICurrentUserAccessor currentUser,
            IIdentityHasher identityHasher,
            IPersonaService personas,
            CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var draft = ToDraft(request, identityHasher);
            var result = await personas.UpdateAsync(id, draft, callerResult.Response!, ct);
            return ToHttpResult(result, ok => Results.Ok(ToResponse(ok)));
        });

        app.MapDelete("/api/personas/{id}", async (
            string id, ICurrentUserAccessor currentUser, IPersonaService personas, CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var result = await personas.DeleteAsync(id, callerResult.Response!, ct);
            return ToHttpResult(result, _ => Results.Ok());
        });

        app.MapPost("/api/personas/{id}/transfer-ownership", async (
            string id,
            TransferOwnershipRequest request,
            ICurrentUserAccessor currentUser,
            IPersonaService personas,
            CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var result = await personas.TransferOwnershipAsync(id, request.NewOwnerEmail, callerResult.Response!, ct);
            return ToHttpResult(result, ok => Results.Ok(ToResponse(ok)));
        }).RequireAuthorization(PolicyNames.RequireAdmin);

        return app;
    }

    /// <summary>
    /// Maps a <see cref="ServerActionResponse{T}"/> to the HTTP shape per
    /// contracts/authorization-policies.md: UNAUTHORIZED is always the same fixed 401 shape
    /// (FR-004); a concurrency conflict is 409; every other ERROR is 400.
    /// </summary>
    private static IResult ToHttpResult<T>(ServerActionResponse<T> result, Func<T, IResult> onSuccess) => result.Status switch
    {
        ResponseStatus.OK => onSuccess(result.Response!),
        ResponseStatus.UNAUTHORIZED => Results.Json(result, statusCode: StatusCodes.Status401Unauthorized),
        ResponseStatus.NOT_FOUND => Results.NotFound(result.Errors),
        ResponseStatus.ERROR when result.Errors.Count > 0 && result.Errors[0].Message == PersonaService.ConcurrencyConflictMessage =>
            Results.Json(result, statusCode: StatusCodes.Status409Conflict),
        _ => Results.BadRequest(result.Errors),
    };

    private static PersonaModel ToDraft(PersonaWriteRequest request, IIdentityHasher identityHasher) => new()
    {
        Id = string.Empty,
        OwnerUserId = string.Empty,
        OwnerPartitionKey = string.Empty,
        Model = request.Model,
        Name = request.Name,
        Description = request.Description,
        PersonaMessage = request.PersonaMessage,
        Extensions = request.Extensions,
        DataProducts = request.DataProducts,
        CollaboratorPartitionKeys = request.CollaboratorEmails.Select(e => identityHasher.ForEmail(e).Value).ToList(),
        SharedWith = request.SharedWith.Select(t => t.Type == "Group"
            ? PersonaShareTarget.ForGroup(t.GroupToken!)
            : PersonaShareTarget.ForIndividual(identityHasher.ForEmail(t.Identity!).Value)).ToList(),
        IsLessonPersona = request.IsLessonPersona,
    };

    private static PersonaResponse ToResponse(PersonaPublicDTO dto) => new(
        dto.Id, dto.OwnerUserId, dto.Model, dto.Name, dto.Description, dto.PersonaMessage,
        dto.Extensions, dto.DataProducts, dto.CollaboratorPartitionKeys, dto.SharedWith,
        dto.A2aEnabled, dto.IsLessonPersona, dto.CreatedAtUtc, dto.UpdatedAtUtc);

    public sealed record PersonaWriteRequest(
        string Model,
        string Name,
        string? Description,
        string PersonaMessage,
        List<string> Extensions,
        List<string> DataProducts,
        List<string> CollaboratorEmails,
        List<PersonaShareTargetRequest> SharedWith,
        bool IsLessonPersona);

    /// <summary><paramref name="Type"/> is <c>"Individual"</c> (with <paramref name="Identity"/> set) or <c>"Group"</c> (with <paramref name="GroupToken"/> set).</summary>
    public sealed record PersonaShareTargetRequest(string Type, string? Identity, string? GroupToken);

    public sealed record TransferOwnershipRequest(string NewOwnerEmail);

    public sealed record PersonaResponse(
        string Id, string OwnerUserId, string Model, string Name, string? Description, string PersonaMessage,
        List<string> Extensions, List<string> DataProducts, List<string> CollaboratorPartitionKeys,
        List<PersonaShareTarget> SharedWith, bool A2aEnabled, bool IsLessonPersona,
        DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
}
