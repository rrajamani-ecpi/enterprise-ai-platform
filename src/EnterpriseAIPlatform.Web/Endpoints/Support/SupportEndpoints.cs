using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.Support;

namespace EnterpriseAIPlatform.Web.Endpoints.Support;

/// <summary>Spec 017's protected route surface — contracts/route-table.md. Health endpoints are wired separately in Program.cs via <c>MapHealthChecks</c>.</summary>
public static class SupportEndpoints
{
    public static IEndpointRouteBuilder MapSupportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/changelog", async (IChangelogReader reader, CancellationToken ct) =>
        {
            var entries = await reader.GetEntriesAsync(ct);
            return Results.Ok(entries.Select(e => new { version = e.Version.ToString(), e.Content }));
        });

        app.MapGet("/api/changelog/acknowledgment", async (
            ICurrentUserAccessor currentUser,
            IIdentityHasher identityHasher,
            IChangelogReader changelogReader,
            IVersionAcknowledgmentStore acknowledgmentStore,
            CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var partitionKey = identityHasher.ForEmail(callerResult.Response!.Email).Value;
            var entries = await changelogReader.GetEntriesAsync(ct);
            var latest = entries.FirstOrDefault();
            var acknowledgment = await acknowledgmentStore.GetAsync(partitionKey, ct);
            var showAlert = AlertWindowEvaluator.ShouldShowAlert(latest, acknowledgment, DateTimeOffset.UtcNow);

            return Results.Ok(new
            {
                latestVersion = latest?.Version.ToString(),
                acknowledgedVersion = acknowledgment?.AcknowledgedVersion,
                acknowledgedAtUtc = acknowledgment?.AcknowledgedAtUtc,
                showAlert,
            });
        });

        app.MapPost("/api/changelog/acknowledgment", async (
            ICurrentUserAccessor currentUser,
            IIdentityHasher identityHasher,
            IChangelogReader changelogReader,
            IVersionAcknowledgmentStore acknowledgmentStore,
            CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var entries = await changelogReader.GetEntriesAsync(ct);
            var latest = entries.FirstOrDefault();
            if (latest is null)
            {
                return Results.BadRequest(new { error = "NO_CHANGELOG_ENTRIES" });
            }

            var partitionKey = identityHasher.ForEmail(callerResult.Response!.Email).Value;

            try
            {
                await acknowledgmentStore.SetAsync(partitionKey, latest.Version.ToString(), ct);
                return Results.Ok();
            }
            catch (Exception)
            {
                // A failed persist must never look like success (D4) — the client is expected to
                // revert its optimistic UI on anything but 2xx.
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Failed to persist acknowledgment.");
            }
        });

        app.MapPost("/api/feedback", async (
            SubmitFeedbackRequest request,
            ICurrentUserAccessor currentUser,
            IIdentityHasher identityHasher,
            IChatThreadStore threadStore,
            IFeedbackForwarder forwarder,
            CancellationToken ct) =>
        {
            var callerResult = currentUser.GetCurrentUser();
            if (callerResult.Status != ResponseStatus.OK)
            {
                return Results.Unauthorized();
            }

            var partitionKey = identityHasher.ForEmail(callerResult.Response!.Email).Value;
            var thread = await threadStore.GetAsync(request.ThreadId, partitionKey, ct);
            if (thread is null)
            {
                // Rejected before any external call (FR-008) — same outcome whether the thread
                // doesn't exist or simply isn't the caller's own; no distinction is leaked either way.
                return Results.Json(new { error = "THREAD_NOT_FOUND_OR_NOT_OWNED" }, statusCode: StatusCodes.Status404NotFound);
            }

            // Forwarding failures are logged only, never surfaced (FR-010) — the return value here
            // is ignored on purpose.
            _ = await forwarder.ForwardAsync(request.ThreadId, request.Content, ct);
            return Results.Ok();
        });

        return app;
    }

    public sealed record SubmitFeedbackRequest(string ThreadId, string Content);
}
