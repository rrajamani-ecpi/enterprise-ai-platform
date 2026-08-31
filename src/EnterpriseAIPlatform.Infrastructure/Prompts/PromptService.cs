using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.Prompts;
using EnterpriseAIPlatform.Application.Sharing;
using EnterpriseAIPlatform.Domain.Prompts;
using EnterpriseAIPlatform.Domain.Sharing;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseAIPlatform.Infrastructure.Prompts;

/// <summary>
/// The single implementation of <see cref="IPromptService"/> (spec 016). Every gated path funnels
/// through <see cref="PromptAccessEvaluator"/>, and owner fields are always server-derived from the
/// caller — never read from request data (Constitution Principle II).
/// </summary>
public sealed class PromptService : IPromptService
{
    /// <summary>
    /// FR-009: one fixed message for both "forbidden" and "does not exist", so the response cannot
    /// be used to enumerate valid prompt ids.
    /// </summary>
    private const string NonRevealingUnauthorizedMessage = "You do not have access to this prompt.";

    // Public so the Web-layer endpoint can distinguish this specific error (mapped to 409) from
    // every other ERROR-status result (mapped to 400) without widening ServerActionResponse itself.
    public const string ConcurrencyConflictMessage = "This prompt was modified concurrently (an ownership transfer may be in progress) — reload and retry.";

    private readonly PromptDbContext _db;
    private readonly IIdentityHasher _identityHasher;
    private readonly ISharingPolicyService _sharingPolicy;

    public PromptService(PromptDbContext db, IIdentityHasher identityHasher, ISharingPolicyService sharingPolicy)
    {
        _db = db;
        _identityHasher = identityHasher;
        _sharingPolicy = sharingPolicy;
    }

    public async Task<ServerActionResponse<IReadOnlyList<PromptWithFavoriteDTO>>> ListAsync(
        UserModel caller, CancellationToken cancellationToken = default)
    {
        var callerPartitionKey = _identityHasher.ForEmail(caller.Email).Value;
        var all = await _db.Prompts.AsNoTracking().ToListAsync(cancellationToken);

        // FR-001/FR-002: the visibility filter is applied server-side, so a prompt the caller cannot
        // read is never serialized to the client at all — not merely hidden by the UI.
        var visible = all.Where(p => PromptAccessEvaluator.CanRead(p, caller, callerPartitionKey)).ToList();

        var favoriteIds = await _db.PromptFavorites.AsNoTracking()
            .Where(f => f.UserPartitionKey == callerPartitionKey)
            .Select(f => f.PromptId)
            .ToListAsync(cancellationToken);
        var favorites = favoriteIds.ToHashSet(StringComparer.Ordinal);

        var results = visible
            .Select(p => new PromptWithFavoriteDTO(PromptPublicDTO.FromModel(p), favorites.Contains(p.Id)))
            .ToList();

        return ServerActionResponse<IReadOnlyList<PromptWithFavoriteDTO>>.Ok(results);
    }

    public async Task<ServerActionResponse<PromptPublicDTO>> GetAsync(
        string promptId, UserModel caller, CancellationToken cancellationToken = default)
    {
        var prompt = await _db.Prompts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == promptId, cancellationToken);
        var callerPartitionKey = _identityHasher.ForEmail(caller.Email).Value;

        // FR-009: the missing-row and access-denied branches are deliberately collapsed. Splitting
        // them would make a 404-vs-401 difference an oracle for "this id exists".
        if (prompt is null || !PromptAccessEvaluator.CanRead(prompt, caller, callerPartitionKey))
        {
            return ServerActionResponse<PromptPublicDTO>.Unauthorized(NonRevealingUnauthorizedMessage);
        }

        return ServerActionResponse<PromptPublicDTO>.Ok(PromptPublicDTO.FromModel(prompt));
    }

    public async Task<ServerActionResponse<PromptPublicDTO>> CreateAsync(
        PromptModel draft, UserModel caller, CancellationToken cancellationToken = default)
    {
        if (!PromptValidationRules.TryValidate(draft.Name, draft.Description, out var validationError))
        {
            return ServerActionResponse<PromptPublicDTO>.Error(validationError!);
        }

        if (!TryValidateShareTargets(draft.SharedWith, caller, out var sharingError))
        {
            return ServerActionResponse<PromptPublicDTO>.Error(sharingError!);
        }

        var now = DateTimeOffset.UtcNow;
        var prompt = new PromptModel
        {
            Id = Guid.NewGuid().ToString("N"),
            // Server-derived from the authenticated caller — a client-supplied owner is ignored.
            OwnerUserId = caller.Email,
            OwnerPartitionKey = _identityHasher.ForEmail(caller.Email).Value,
            Name = draft.Name,
            Description = draft.Description,
            CollaboratorPartitionKeys = draft.CollaboratorPartitionKeys,
            SharedWith = draft.SharedWith,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        _db.Prompts.Add(prompt);
        await _db.SaveChangesAsync(cancellationToken);

        return ServerActionResponse<PromptPublicDTO>.Ok(PromptPublicDTO.FromModel(prompt));
    }

    public async Task<ServerActionResponse<PromptPublicDTO>> UpdateAsync(
        string promptId, PromptModel draft, UserModel caller, CancellationToken cancellationToken = default)
    {
        var prompt = await _db.Prompts.FirstOrDefaultAsync(p => p.Id == promptId, cancellationToken);
        var callerPartitionKey = _identityHasher.ForEmail(caller.Email).Value;

        // FR-009: collapsed branch — a read-only sharee gets the identical response to a caller
        // naming an id that has never existed.
        if (prompt is null || !PromptAccessEvaluator.CanWrite(prompt, caller, callerPartitionKey))
        {
            return ServerActionResponse<PromptPublicDTO>.Unauthorized(NonRevealingUnauthorizedMessage);
        }

        if (!PromptValidationRules.TryValidate(draft.Name, draft.Description, out var validationError))
        {
            return ServerActionResponse<PromptPublicDTO>.Error(validationError!);
        }

        if (!TryValidateShareTargets(draft.SharedWith, caller, out var sharingError))
        {
            return ServerActionResponse<PromptPublicDTO>.Error(sharingError!);
        }

        prompt.Name = draft.Name;
        prompt.Description = draft.Description;
        prompt.CollaboratorPartitionKeys = draft.CollaboratorPartitionKeys;
        prompt.SharedWith = draft.SharedWith;
        // Ownership fields are deliberately absent from this list: an edit can never change the
        // owner, and a transfer can never change content (FR-005).
        prompt.UpdatedAtUtc = DateTimeOffset.UtcNow;
        prompt.RowVersion = Guid.NewGuid();

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServerActionResponse<PromptPublicDTO>.Error(ConcurrencyConflictMessage);
        }

        return ServerActionResponse<PromptPublicDTO>.Ok(PromptPublicDTO.FromModel(prompt));
    }

    public async Task<ServerActionResponse<bool>> DeleteAsync(
        string promptId, UserModel caller, CancellationToken cancellationToken = default)
    {
        var prompt = await _db.Prompts.FirstOrDefaultAsync(p => p.Id == promptId, cancellationToken);
        var callerPartitionKey = _identityHasher.ForEmail(caller.Email).Value;

        if (prompt is null || !PromptAccessEvaluator.CanWrite(prompt, caller, callerPartitionKey))
        {
            return ServerActionResponse<bool>.Unauthorized(NonRevealingUnauthorizedMessage);
        }

        // FR-017: favorites of this prompt are removed by the schema's cascade — no explicit
        // cleanup here, so no delete path can forget it (SC-006).
        _db.Prompts.Remove(prompt);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServerActionResponse<bool>.Error(ConcurrencyConflictMessage);
        }

        return ServerActionResponse<bool>.Ok(true);
    }

    public async Task<ServerActionResponse<PromptPublicDTO>> TransferOwnershipAsync(
        string promptId, string newOwnerEmail, UserModel caller, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newOwnerEmail))
        {
            return ServerActionResponse<PromptPublicDTO>.Error("A new owner is required.");
        }

        var prompt = await _db.Prompts.FirstOrDefaultAsync(p => p.Id == promptId, cancellationToken);
        var callerPartitionKey = _identityHasher.ForEmail(caller.Email).Value;

        // FR-006 + FR-009. Note this deliberately diverges from spec 009's PersonaService, which
        // returns NotFound for a missing row here: that leaks existence, which FR-009 forbids.
        // The gate is also in-handler rather than a route-level RequireAdmin policy, because the
        // owner (who is not necessarily an admin) must be allowed through.
        if (prompt is null || !PromptAccessEvaluator.CanTransfer(prompt, caller, callerPartitionKey))
        {
            return ServerActionResponse<PromptPublicDTO>.Unauthorized(NonRevealingUnauthorizedMessage);
        }

        // FR-008: a single atomic row update. Id never changes, so there is no delete-then-recreate
        // step and no window in which zero or two copies of the prompt exist. A retry re-reads
        // current state first (the fetch above), so it either safely reapplies the same owner
        // (harmless) or proceeds normally — never a stale write, never a duplicate.
        // Only ownership fields are touched: name, description, sharing, collaborators, favorites,
        // and CreatedAtUtc are all left exactly as they were (FR-005/FR-017).
        prompt.OwnerUserId = newOwnerEmail;
        prompt.OwnerPartitionKey = _identityHasher.ForEmail(newOwnerEmail).Value;
        prompt.UpdatedAtUtc = DateTimeOffset.UtcNow;
        // FR-007: regenerating the token is what makes a concurrent double-submit fail rather than
        // silently apply twice — no separate in-flight flag is needed.
        prompt.RowVersion = Guid.NewGuid();

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServerActionResponse<PromptPublicDTO>.Error(ConcurrencyConflictMessage);
        }

        return ServerActionResponse<PromptPublicDTO>.Ok(PromptPublicDTO.FromModel(prompt));
    }

    public async Task<ServerActionResponse<IReadOnlyList<PromptPublicDTO>>> ListFavoritesAsync(
        UserModel caller, CancellationToken cancellationToken = default)
    {
        var callerPartitionKey = _identityHasher.ForEmail(caller.Email).Value;

        var favoriteIds = await _db.PromptFavorites.AsNoTracking()
            .Where(f => f.UserPartitionKey == callerPartitionKey)
            .Select(f => f.PromptId)
            .ToListAsync(cancellationToken);

        var prompts = await _db.Prompts.AsNoTracking()
            .Where(p => favoriteIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        // Re-filtered through the access gate: a prompt that was favorited and later un-shared must
        // drop out of the caller's favorites view rather than remain readable through it.
        var visible = prompts
            .Where(p => PromptAccessEvaluator.CanRead(p, caller, callerPartitionKey))
            .Select(PromptPublicDTO.FromModel)
            .ToList();

        return ServerActionResponse<IReadOnlyList<PromptPublicDTO>>.Ok(visible);
    }

    public async Task<ServerActionResponse<bool>> AddFavoriteAsync(
        string promptId, UserModel caller, CancellationToken cancellationToken = default)
    {
        var prompt = await _db.Prompts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == promptId, cancellationToken);
        var callerPartitionKey = _identityHasher.ForEmail(caller.Email).Value;

        if (prompt is null || !PromptAccessEvaluator.CanRead(prompt, caller, callerPartitionKey))
        {
            return ServerActionResponse<bool>.Unauthorized(NonRevealingUnauthorizedMessage);
        }

        var existing = await _db.PromptFavorites
            .FirstOrDefaultAsync(f => f.UserPartitionKey == callerPartitionKey && f.PromptId == promptId, cancellationToken);

        if (existing is not null)
        {
            return ServerActionResponse<bool>.Ok(true);
        }

        _db.PromptFavorites.Add(new PromptFavorite
        {
            UserPartitionKey = callerPartitionKey,
            PromptId = promptId,
            FavoritedAtUtc = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync(cancellationToken);
        return ServerActionResponse<bool>.Ok(true);
    }

    public async Task<ServerActionResponse<bool>> RemoveFavoriteAsync(
        string promptId, UserModel caller, CancellationToken cancellationToken = default)
    {
        var callerPartitionKey = _identityHasher.ForEmail(caller.Email).Value;

        // Scoped to the caller's own row by the composite key, so one user can never remove
        // another's favorite (SC-007). Idempotent: removing a favorite that isn't there succeeds.
        var existing = await _db.PromptFavorites
            .FirstOrDefaultAsync(f => f.UserPartitionKey == callerPartitionKey && f.PromptId == promptId, cancellationToken);

        if (existing is null)
        {
            return ServerActionResponse<bool>.Ok(true);
        }

        _db.PromptFavorites.Remove(existing);
        await _db.SaveChangesAsync(cancellationToken);

        return ServerActionResponse<bool>.Ok(true);
    }

    /// <summary>
    /// FR-004: every share target on every write is validated by spec 018's
    /// <see cref="ISharingPolicyService"/>. There is deliberately no prompt-local sharing rule
    /// anywhere in this feature — the policy has exactly one implementation platform-wide.
    /// </summary>
    private bool TryValidateShareTargets(IEnumerable<PromptShareTarget> targets, UserModel caller, out string? error)
    {
        foreach (var target in targets)
        {
            var request = target.Type == ShareTargetType.Group
                ? new ShareTargetRequest(ShareTargetType.Group, target.GroupToken)
                : new ShareTargetRequest(ShareTargetType.Individual);

            var decision = _sharingPolicy.Evaluate(caller, request);
            if (!decision.IsAllowed)
            {
                // Surfacing the reason keeps the denial actionable, and keeps the explanation
                // owned by spec 018 rather than restated here.
                error = $"Sharing denied: {decision.Reason}.";
                return false;
            }
        }

        error = null;
        return true;
    }
}
