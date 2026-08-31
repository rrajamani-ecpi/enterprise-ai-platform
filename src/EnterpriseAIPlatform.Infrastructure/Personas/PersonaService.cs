using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.Personas;
using EnterpriseAIPlatform.Domain.Personas;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseAIPlatform.Infrastructure.Personas;

/// <summary>
/// The single implementation of <see cref="IPersonaService"/> and (internal)
/// <see cref="IPersonaRawAccessor"/> (spec 009). Every <see cref="IPersonaService"/> method
/// returns <see cref="PersonaPublicDTO"/>; only the internal <see cref="IPersonaRawAccessor"/>
/// surface can return a raw <see cref="PersonaModel"/> (FR-009/FR-010).
/// </summary>
public sealed class PersonaService : IPersonaService, IPersonaRawAccessor
{
    private const string NonRevealingUnauthorizedMessage = "You do not have access to this persona.";
    private const string LessonPersonaBlockedMessage = "This persona is a lesson persona and can only be modified by an admin.";
    // Public so the Web-layer endpoint can distinguish this specific error (mapped to 409) from
    // every other ERROR-status result (mapped to 400) without widening ServerActionResponse itself.
    public const string ConcurrencyConflictMessage = "This persona was modified concurrently (an ownership transfer may be in progress) — reload and retry.";
    private const string GroupShareForbiddenMessage = "Only admins may share a persona with a group token.";

    private readonly PersonaDbContext _db;
    private readonly IIdentityHasher _identityHasher;

    public PersonaService(PersonaDbContext db, IIdentityHasher identityHasher)
    {
        _db = db;
        _identityHasher = identityHasher;
    }

    public async Task<ServerActionResponse<PersonaPublicDTO>> GetAsync(string personaId, UserModel caller, CancellationToken cancellationToken = default)
    {
        var persona = await _db.Personas.AsNoTracking().FirstOrDefaultAsync(p => p.Id == personaId, cancellationToken);
        var callerPartitionKey = _identityHasher.ForEmail(caller.Email).Value;

        if (persona is null || PersonaAccessEvaluator.Evaluate(persona, caller, callerPartitionKey) == PersonaAccessResult.Denied)
        {
            return ServerActionResponse<PersonaPublicDTO>.Unauthorized(NonRevealingUnauthorizedMessage);
        }

        return ServerActionResponse<PersonaPublicDTO>.Ok(PersonaPublicDTO.FromModel(persona));
    }

    public async Task<ServerActionResponse<IReadOnlyList<PersonaPublicDTO>>> ListAsync(UserModel caller, CancellationToken cancellationToken = default)
    {
        var callerPartitionKey = _identityHasher.ForEmail(caller.Email).Value;
        var all = await _db.Personas.AsNoTracking().ToListAsync(cancellationToken);

        // FR-007: lesson personas never appear in a non-admin's listing, regardless of ownership/
        // collaborator status — reachable only via a direct Canvas/LTI deep link (spec Assumptions).
        var visible = caller.IsAdmin
            ? all
            : all.Where(p => !p.IsLessonPersona
                              && PersonaAccessEvaluator.Evaluate(p, caller, callerPartitionKey) == PersonaAccessResult.FullAccess);

        return ServerActionResponse<IReadOnlyList<PersonaPublicDTO>>.Ok(visible.Select(PersonaPublicDTO.FromModel).ToList());
    }

    public async Task<ServerActionResponse<PersonaPublicDTO>> CreateAsync(PersonaModel draft, UserModel caller, CancellationToken cancellationToken = default)
    {
        if (!PersonaExtensionRules.TryValidate(draft.Extensions, draft.DataProducts, out var validationError))
        {
            return ServerActionResponse<PersonaPublicDTO>.Error(validationError!);
        }

        if (draft.SharedWith.Any(t => t.Type == PersonaShareTargetType.Group) && !PersonaAccessEvaluator.CanShareGroupTarget(caller))
        {
            return ServerActionResponse<PersonaPublicDTO>.Error(GroupShareForbiddenMessage);
        }

        var now = DateTimeOffset.UtcNow;
        var persona = new PersonaModel
        {
            Id = Guid.NewGuid().ToString("N"),
            OwnerUserId = caller.Email,
            OwnerPartitionKey = _identityHasher.ForEmail(caller.Email).Value,
            Model = draft.Model,
            Name = draft.Name,
            Description = draft.Description,
            PersonaMessage = draft.PersonaMessage,
            Extensions = draft.Extensions,
            DataProducts = draft.DataProducts,
            CollaboratorPartitionKeys = draft.CollaboratorPartitionKeys,
            SharedWith = draft.SharedWith,
            ApiKey = null, // FR-009/FR-010: server-managed; no mechanism to set it exists in this build (spec 011).
            A2aEnabled = false,
            IsLessonPersona = caller.IsAdmin && draft.IsLessonPersona,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        _db.Personas.Add(persona);
        await _db.SaveChangesAsync(cancellationToken);

        return ServerActionResponse<PersonaPublicDTO>.Ok(PersonaPublicDTO.FromModel(persona));
    }

    public async Task<ServerActionResponse<PersonaPublicDTO>> UpdateAsync(string personaId, PersonaModel draft, UserModel caller, CancellationToken cancellationToken = default)
    {
        var persona = await _db.Personas.FirstOrDefaultAsync(p => p.Id == personaId, cancellationToken);
        var callerPartitionKey = _identityHasher.ForEmail(caller.Email).Value;

        if (persona is null || PersonaAccessEvaluator.Evaluate(persona, caller, callerPartitionKey) == PersonaAccessResult.Denied)
        {
            return ServerActionResponse<PersonaPublicDTO>.Unauthorized(NonRevealingUnauthorizedMessage);
        }

        if (!PersonaAccessEvaluator.CanWrite(persona, caller, callerPartitionKey, PersonaOperation.Edit))
        {
            return ServerActionResponse<PersonaPublicDTO>.Error(LessonPersonaBlockedMessage);
        }

        if (!PersonaExtensionRules.TryValidate(draft.Extensions, draft.DataProducts, out var validationError))
        {
            return ServerActionResponse<PersonaPublicDTO>.Error(validationError!);
        }

        if (draft.SharedWith.Any(t => t.Type == PersonaShareTargetType.Group) && !PersonaAccessEvaluator.CanShareGroupTarget(caller))
        {
            return ServerActionResponse<PersonaPublicDTO>.Error(GroupShareForbiddenMessage);
        }

        persona.Model = draft.Model;
        persona.Name = draft.Name;
        persona.Description = draft.Description;
        persona.PersonaMessage = draft.PersonaMessage;
        persona.Extensions = draft.Extensions;
        persona.DataProducts = draft.DataProducts;
        persona.CollaboratorPartitionKeys = draft.CollaboratorPartitionKeys;
        persona.SharedWith = draft.SharedWith;
        // FR-006: a non-admin's submitted IsLessonPersona is discarded — the existing value is preserved.
        persona.IsLessonPersona = caller.IsAdmin ? draft.IsLessonPersona : persona.IsLessonPersona;
        persona.UpdatedAtUtc = DateTimeOffset.UtcNow;
        persona.RowVersion = Guid.NewGuid();

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServerActionResponse<PersonaPublicDTO>.Error(ConcurrencyConflictMessage);
        }

        return ServerActionResponse<PersonaPublicDTO>.Ok(PersonaPublicDTO.FromModel(persona));
    }

    public async Task<ServerActionResponse<bool>> DeleteAsync(string personaId, UserModel caller, CancellationToken cancellationToken = default)
    {
        var persona = await _db.Personas.FirstOrDefaultAsync(p => p.Id == personaId, cancellationToken);
        var callerPartitionKey = _identityHasher.ForEmail(caller.Email).Value;

        if (persona is null || PersonaAccessEvaluator.Evaluate(persona, caller, callerPartitionKey) == PersonaAccessResult.Denied)
        {
            return ServerActionResponse<bool>.Unauthorized(NonRevealingUnauthorizedMessage);
        }

        if (!PersonaAccessEvaluator.CanWrite(persona, caller, callerPartitionKey, PersonaOperation.Delete))
        {
            return ServerActionResponse<bool>.Error(LessonPersonaBlockedMessage);
        }

        _db.Personas.Remove(persona);

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

    public async Task<ServerActionResponse<PersonaPublicDTO>> TransferOwnershipAsync(string personaId, string newOwnerEmail, UserModel caller, CancellationToken cancellationToken = default)
    {
        var persona = await _db.Personas.FirstOrDefaultAsync(p => p.Id == personaId, cancellationToken);
        if (persona is null)
        {
            return ServerActionResponse<PersonaPublicDTO>.NotFound("Persona not found.");
        }

        // Single atomic row update (research.md D1) — Id never changes, so there is no
        // delete-then-recreate step and no way to end up with zero or two copies. A retry (whether
        // after success or failure) always re-reads current state first (the fetch above), so it
        // either safely reapplies the same owner (harmless) or proceeds normally — never a stale
        // write, never a duplicate (FR-002).
        persona.OwnerUserId = newOwnerEmail;
        persona.OwnerPartitionKey = _identityHasher.ForEmail(newOwnerEmail).Value;
        persona.UpdatedAtUtc = DateTimeOffset.UtcNow;
        persona.RowVersion = Guid.NewGuid();

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServerActionResponse<PersonaPublicDTO>.Error(ConcurrencyConflictMessage);
        }

        return ServerActionResponse<PersonaPublicDTO>.Ok(PersonaPublicDTO.FromModel(persona));
    }

    async Task<ServerActionResponse<PersonaModel>> IPersonaRawAccessor.GetRawAsync(string personaId, CancellationToken cancellationToken)
    {
        var persona = await _db.Personas.AsNoTracking().FirstOrDefaultAsync(p => p.Id == personaId, cancellationToken);
        return persona is null
            ? ServerActionResponse<PersonaModel>.NotFound("Persona not found.")
            : ServerActionResponse<PersonaModel>.Ok(persona);
    }
}
