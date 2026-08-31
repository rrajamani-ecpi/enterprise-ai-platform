using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Domain.Personas;

namespace EnterpriseAIPlatform.Application.Personas;

/// <summary>
/// The single implementation of spec 009's persona access gate (FR-003–FR-005, FR-008;
/// Constitution Principle IV) — framework-free so the full role x ownership x lesson-persona
/// matrix (SC-002/SC-003) is unit-testable without any infrastructure, mirroring spec 018's
/// <c>SharingPolicyEvaluator</c>/spec 014's <c>ModelAccessEvaluator</c>.
/// </summary>
/// <remarks>
/// Takes <paramref name="callerPartitionKey"/> as a precomputed value (via
/// <c>IIdentityHasher.ForEmail</c>, spec 002) rather than hashing internally — keeps this class
/// DI-free (research.md D3) without duplicating the one canonical hashing implementation
/// (research.md D2).
/// </remarks>
public static class PersonaAccessEvaluator
{
    /// <summary>Admin, owner, and hashed collaborators get full access; an eligible student gets read-only on a lesson persona; everyone else is denied (FR-003).</summary>
    public static PersonaAccessResult Evaluate(PersonaModel persona, UserModel caller, string callerPartitionKey)
    {
        if (caller.IsAdmin
            || callerPartitionKey == persona.OwnerPartitionKey
            || persona.CollaboratorPartitionKeys.Contains(callerPartitionKey))
        {
            return PersonaAccessResult.FullAccess;
        }

        if (persona.IsLessonPersona && caller.IsStudent)
        {
            return PersonaAccessResult.ReadOnly;
        }

        return PersonaAccessResult.Denied;
    }

    /// <summary>
    /// Requires <see cref="PersonaAccessResult.FullAccess"/> from <see cref="Evaluate"/>, and
    /// additionally blocks a non-admin's edit/delete of a lesson persona even when they're a
    /// designated collaborator (FR-005) — evaluated after, not instead of, the base access check.
    /// </summary>
    public static bool CanWrite(PersonaModel persona, UserModel caller, string callerPartitionKey, PersonaOperation operation)
    {
        if (Evaluate(persona, caller, callerPartitionKey) != PersonaAccessResult.FullAccess)
        {
            return false;
        }

        if (persona.IsLessonPersona && !caller.IsAdmin && operation is PersonaOperation.Edit or PersonaOperation.Delete)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// FR-008: admins may share with group tokens; non-admins may share only with individuals.
    /// No override mechanism exists in this build — a non-admin's group-token share attempt is
    /// rejected unconditionally (resolved via clarification; research.md D8's addendum). This
    /// spec's own binary rule — does not consume spec 018's <c>ISharingPolicyService</c>.
    /// </summary>
    public static bool CanShareGroupTarget(UserModel caller) => caller.IsAdmin;
}
