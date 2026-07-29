namespace EnterpriseAIPlatform.Application.Common;

/// <summary>Status codes for the app-wide <see cref="ServerActionResponse{T}"/> envelope (spec 002 FR-005/006).</summary>
public enum ResponseStatus
{
    OK,
    ERROR,
    NOT_FOUND,
    UNAUTHORIZED,
}
