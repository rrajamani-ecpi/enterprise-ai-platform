namespace EnterpriseAIPlatform.Application.Common;

/// <summary>
/// The app-wide structured result envelope (spec 002 FR-005/006). Callers branch on
/// <see cref="Status"/> instead of catching exceptions for expected failure modes
/// (Constitution Principle III — fail loud, never fabricate success).
/// </summary>
public sealed record ServerActionResponse<T>
{
    public required ResponseStatus Status { get; init; }

    public T? Response { get; init; }

    public IReadOnlyList<ActionError> Errors { get; init; } = Array.Empty<ActionError>();

    public bool IsSuccess => Status == ResponseStatus.OK;

    public static ServerActionResponse<T> Ok(T response) =>
        new() { Status = ResponseStatus.OK, Response = response };

    public static ServerActionResponse<T> Unauthorized(string message = "No active session.") =>
        new() { Status = ResponseStatus.UNAUTHORIZED, Errors = new[] { new ActionError(message) } };

    public static ServerActionResponse<T> Error(string message) =>
        new() { Status = ResponseStatus.ERROR, Errors = new[] { new ActionError(message) } };

    public static ServerActionResponse<T> NotFound(string message) =>
        new() { Status = ResponseStatus.NOT_FOUND, Errors = new[] { new ActionError(message) } };
}
