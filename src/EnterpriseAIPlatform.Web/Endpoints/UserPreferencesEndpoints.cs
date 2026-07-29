using System.Collections.Concurrent;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;

namespace EnterpriseAIPlatform.Web.Endpoints;

/// <summary>
/// Spec 014 FR-010/US5: every route here performs its own explicit authenticated-user check and
/// returns the structured <c>UNAUTHORIZED</c> shape (matching spec 002's <c>GetCurrentUser()</c>
/// envelope) rather than relying solely on an internal helper's uncaught throw producing a 500.
/// Preference storage itself is a minimal in-memory placeholder — no persistence design is
/// specified by spec 014, whose scope here is the auth-check shape, not the storage layer.
/// </summary>
public static class UserPreferencesEndpoints
{
    private static readonly ConcurrentDictionary<string, UserPreferences> Store = new();

    public static IEndpointRouteBuilder MapUserPreferencesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/user/preferences", (ICurrentUserAccessor currentUser) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Json(callerResult, statusCode: StatusCodes.Status401Unauthorized);
            }

            var preferences = Store.GetValueOrDefault(callerResult.Response!.Email, UserPreferences.Default);
            return Results.Ok(preferences);
        });

        app.MapPut("/api/user/preferences", (UserPreferences preferences, ICurrentUserAccessor currentUser) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Json(callerResult, statusCode: StatusCodes.Status401Unauthorized);
            }

            Store[callerResult.Response!.Email] = preferences;
            return Results.Ok(preferences);
        });

        return app;
    }

    public sealed record UserPreferences(string Theme)
    {
        public static UserPreferences Default => new("system");
    }
}
