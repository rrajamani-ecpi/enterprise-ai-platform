using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Domain.Personas;

namespace EnterpriseAIPlatform.Application.Personas;

/// <summary>
/// The single, server-side persona CRUD + authorization surface (spec 009 FR-001–FR-013) that
/// specs 010/011 build on. Every method returns <see cref="PersonaPublicDTO"/>, never
/// <see cref="PersonaModel"/> — structurally impossible for <c>ApiKey</c> to reach a caller
/// through this interface (FR-009). Exactly one implementation (architecture-tested).
/// </summary>
public interface IPersonaService
{
    /// <summary>
    /// Enumeration-safe: returns <see cref="ResponseStatus.UNAUTHORIZED"/> with a fixed message —
    /// never <see cref="ResponseStatus.NOT_FOUND"/> — for both "doesn't exist" and "exists but
    /// forbidden" (FR-004, research.md D4).
    /// </summary>
    Task<ServerActionResponse<PersonaPublicDTO>> GetAsync(string personaId, UserModel caller, CancellationToken cancellationToken = default);

    /// <summary>Non-admin callers never see lesson personas in the result (FR-007).</summary>
    Task<ServerActionResponse<IReadOnlyList<PersonaPublicDTO>>> ListAsync(UserModel caller, CancellationToken cancellationToken = default);

    /// <summary>
    /// <paramref name="draft"/> carries only caller-settable fields (Name/Description/
    /// PersonaMessage/Model/Extensions/DataProducts/CollaboratorPartitionKeys/SharedWith) —
    /// server-managed fields (Id, OwnerPartitionKey, ApiKey, RowVersion, CreatedAtUtc/UpdatedAtUtc,
    /// IsLessonPersona) are always ignored/overwritten, never trusted from the caller.
    /// Validated via <see cref="PersonaExtensionRules.TryValidate"/> before persistence (FR-011).
    /// </summary>
    Task<ServerActionResponse<PersonaPublicDTO>> CreateAsync(PersonaModel draft, UserModel caller, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same <paramref name="draft"/> contract as <see cref="CreateAsync"/>. A non-admin caller's
    /// <c>IsLessonPersona</c> value is discarded server-side (FR-006); a lesson-persona write
    /// attempt by a non-admin — including a designated collaborator — is rejected (FR-005). A
    /// concurrent write since the caller last read the persona surfaces as a specific conflict
    /// error, never a silent overwrite (FR-013, research.md D7).
    /// </summary>
    Task<ServerActionResponse<PersonaPublicDTO>> UpdateAsync(string personaId, PersonaModel draft, UserModel caller, CancellationToken cancellationToken = default);

    /// <summary>Lesson-persona deletion by a non-admin — including a designated collaborator — is rejected (FR-005).</summary>
    Task<ServerActionResponse<bool>> DeleteAsync(string personaId, UserModel caller, CancellationToken cancellationToken = default);

    /// <summary>
    /// Admin-only (route-declarable <c>RequireAdmin</c>). A single atomic row update (research.md
    /// D1) — never a delete-then-recreate. Idempotent: retrying after success is a harmless no-op;
    /// retrying after failure proceeds normally (FR-002). A concurrent edit/delete racing this
    /// surfaces as a specific conflict error (FR-013).
    /// </summary>
    Task<ServerActionResponse<PersonaPublicDTO>> TransferOwnershipAsync(string personaId, string newOwnerEmail, UserModel caller, CancellationToken cancellationToken = default);
}
