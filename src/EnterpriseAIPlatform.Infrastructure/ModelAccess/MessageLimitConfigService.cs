using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.ModelAccess;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseAIPlatform.Infrastructure.ModelAccess;

/// <summary>
/// The single implementation of the message-limit config (spec 014 FR-004/006/008). Server-side
/// re-validation happens here regardless of caller — there is no admin UI in R1 to bypass, and
/// there won't be a second validation path when one is added in R2 (Principle V).
/// </summary>
public sealed class MessageLimitConfigService : IMessageLimitConfigService
{
    private readonly ModelAccessDbContext _db;

    public MessageLimitConfigService(ModelAccessDbContext db) => _db = db;

    public async Task<ServerActionResponse<MessageLimitConfig>> GetAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _db.MessageLimitConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == 1, cancellationToken);

        // Fresh-tenant default: both caps disabled, never an error (Edge Case).
        return ServerActionResponse<MessageLimitConfig>.Ok(existing ?? new MessageLimitConfig());
    }

    public async Task<ServerActionResponse<bool>> SetAsync(
        int? perMessageCharacterCap,
        int? dailyMessageCap,
        string updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        if (perMessageCharacterCap is int perMessage && perMessage < 1)
        {
            return ServerActionResponse<bool>.Error("perMessageCharacterCap must be an integer >= 1 when set.");
        }

        if (dailyMessageCap is int daily && daily < 1)
        {
            return ServerActionResponse<bool>.Error("dailyMessageCap must be an integer >= 1 when set.");
        }

        var existing = await _db.MessageLimitConfigs.FirstOrDefaultAsync(c => c.Id == 1, cancellationToken);
        if (existing is null)
        {
            existing = new MessageLimitConfig();
            _db.MessageLimitConfigs.Add(existing);
        }

        existing.PerMessageCharacterCap = perMessageCharacterCap;
        existing.DailyMessageCap = dailyMessageCap;
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        existing.UpdatedByUserId = updatedByUserId;

        await _db.SaveChangesAsync(cancellationToken);
        return ServerActionResponse<bool>.Ok(true);
    }
}
