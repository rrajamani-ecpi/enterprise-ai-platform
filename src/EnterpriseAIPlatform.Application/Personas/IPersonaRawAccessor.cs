using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Domain.Personas;

namespace EnterpriseAIPlatform.Application.Personas;

/// <summary>
/// The one legitimate path to a full <see cref="PersonaModel"/> (including <c>ApiKey</c>),
/// reserved for spec 011's future A2A credential comparison (FR-010). Deliberately <c>internal</c>
/// — a compiler-enforced restriction, not a source-scan/naming convention (research.md D5,
/// analyze T1 finding). <c>EnterpriseAIPlatform.Web</c> has no <c>InternalsVisibleTo</c> grant, so
/// it cannot reference this interface at all.
/// </summary>
internal interface IPersonaRawAccessor
{
    Task<ServerActionResponse<PersonaModel>> GetRawAsync(string personaId, CancellationToken cancellationToken = default);
}
