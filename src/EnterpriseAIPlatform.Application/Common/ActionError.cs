namespace EnterpriseAIPlatform.Application.Common;

/// <summary>A single, user-safe error message carried on a failed <see cref="ServerActionResponse{T}"/>.</summary>
public sealed record ActionError(string Message);
