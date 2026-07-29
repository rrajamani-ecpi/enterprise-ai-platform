using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Domain.ModelAccess;

namespace EnterpriseAIPlatform.Application.ModelAccess;

/// <summary>
/// Message-limit config (spec 014 FR-004/006/008). <see cref="GetAsync"/> is open to any
/// authenticated caller; the endpoint layer applies <c>RequireAdmin</c> to <see cref="SetAsync"/>,
/// not this service. <see cref="SetAsync"/> re-validates server-side regardless of client input.
/// </summary>
public interface IMessageLimitConfigService
{
    /// <summary>Returns sensible defaults (both caps disabled) if no config exists yet (Edge Case).</summary>
    Task<ServerActionResponse<MessageLimitConfig>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Rejects any non-null cap that is not an integer &gt;= 1 (FR-008).</summary>
    Task<ServerActionResponse<bool>> SetAsync(
        int? perMessageCharacterCap,
        int? dailyMessageCap,
        string updatedByUserId,
        CancellationToken cancellationToken = default);
}
