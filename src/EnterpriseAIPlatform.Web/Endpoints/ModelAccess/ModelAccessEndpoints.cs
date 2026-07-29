using EnterpriseAIPlatform.Application.Authorization;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.ModelAccess;

namespace EnterpriseAIPlatform.Web.Endpoints.ModelAccess;

/// <summary>
/// Spec 014's route surface — contracts/route-table.md. Read endpoints use the inherited
/// authenticated-user fallback policy (spec 002); every write endpoint is explicitly mapped to
/// <see cref="PolicyNames.RequireAdmin"/> (FR-001/006/007).
/// </summary>
public static class ModelAccessEndpoints
{
    public static IEndpointRouteBuilder MapModelAccessEndpoints(this IEndpointRouteBuilder app)
    {
        // --- Reads: any authenticated caller (FR-004/005) ---

        app.MapGet("/api/model-access/available-models", async (
            ICurrentUserAccessor currentUser, IModelAccessService modelAccess, CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var result = await modelAccess.GetAvailableModelsAsync(callerResult.Response!, ct);
            return Results.Ok(result.Response);
        });

        app.MapGet("/api/model-catalog", async (IModelCatalogService catalog, CancellationToken ct) =>
        {
            var result = await catalog.ListAsync(includeDeleted: false, ct);
            return Results.Ok(result.Response);
        });

        app.MapGet("/api/config/message-limit", async (IMessageLimitConfigService service, CancellationToken ct) =>
        {
            var result = await service.GetAsync(ct);
            return Results.Ok(result.Response);
        });

        app.MapGet("/api/config/persona-generation-model", async (
            IPersonaGenerationModelConfigService service, CancellationToken ct) =>
        {
            var result = await service.GetAsync(ct);
            return Results.Ok(result.Response);
        });

        // --- Writes: RequireAdmin (FR-001/006/007) ---

        app.MapPut("/api/admin/system-config", async (
            SystemModelConfig config, ISystemModelConfigStore store, ISystemModelConfigCache cache, CancellationToken ct) =>
        {
            await store.SaveAsync(config, ct);
            await cache.InvalidateAsync(ct);
            return Results.Ok();
        }).RequireAuthorization(PolicyNames.RequireAdmin);

        app.MapPut("/api/admin/model-config/{id}", async (
            string id, ModelConfigDocument model, IModelCatalogService catalog, CancellationToken ct) =>
        {
            model.Id = id;
            var result = await catalog.UpsertAsync(model, ct);
            return result.Status == ResponseStatus.OK ? Results.Ok() : Results.BadRequest(result.Errors);
        }).RequireAuthorization(PolicyNames.RequireAdmin);

        app.MapDelete("/api/admin/model-config/{id}", async (
            string id, IModelCatalogService catalog, CancellationToken ct) =>
        {
            var result = await catalog.SoftDeleteAsync(id, ct);
            return result.Status == ResponseStatus.OK ? Results.Ok() : Results.NotFound(result.Errors);
        }).RequireAuthorization(PolicyNames.RequireAdmin);

        app.MapPut("/api/admin/config/message-limit", async (
            SetMessageLimitRequest request,
            ICurrentUserAccessor currentUser,
            IMessageLimitConfigService service,
            CancellationToken ct) =>
        {
            var caller = currentUser.GetCurrentUser();
            var result = await service.SetAsync(
                request.PerMessageCharacterCap, request.DailyMessageCap, caller.Response?.Email ?? "unknown", ct);
            return result.Status == ResponseStatus.OK ? Results.Ok() : Results.BadRequest(result.Errors);
        }).RequireAuthorization(PolicyNames.RequireAdmin);

        app.MapPut("/api/admin/config/persona-generation-model", async (
            SetPersonaGenerationModelRequest request,
            ICurrentUserAccessor currentUser,
            IPersonaGenerationModelConfigService service,
            CancellationToken ct) =>
        {
            var caller = currentUser.GetCurrentUser();
            var result = await service.SetAllowedModelsAsync(
                request.AllowedModelIds, caller.Response?.Email ?? "unknown", ct);
            return result.Status == ResponseStatus.OK ? Results.Ok() : Results.BadRequest(result.Errors);
        }).RequireAuthorization(PolicyNames.RequireAdmin);

        return app;
    }

    public sealed record SetMessageLimitRequest(int? PerMessageCharacterCap, int? DailyMessageCap);

    public sealed record SetPersonaGenerationModelRequest(IReadOnlyList<string> AllowedModelIds);
}
