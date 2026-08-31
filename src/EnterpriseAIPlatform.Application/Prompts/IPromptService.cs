using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Domain.Prompts;

namespace EnterpriseAIPlatform.Application.Prompts;

/// <summary>
/// The single Application-layer seam for prompt persistence (spec 016
/// contracts/service-interfaces.md). Every method takes the caller's server-derived
/// <see cref="UserModel"/> — no method infers identity from ambient state, and no method accepts a
/// caller identity from request data (Constitution Principle II).
/// </summary>
/// <remarks>
/// Gated paths return a fixed, non-revealing <c>UNAUTHORIZED</c> for both "forbidden" and "does not
/// exist" (FR-009). <c>NOT_FOUND</c> is never returned from them: it maps to HTTP 404 while a denial
/// maps to 401, so the status code alone would let an unauthorized caller enumerate valid prompt
/// ids (contracts/authorization-policies.md).
/// </remarks>
public interface IPromptService
{
    /// <summary>Prompts the caller may read (FR-001/FR-002), filtered server-side.</summary>
    Task<ServerActionResponse<IReadOnlyList<PromptWithFavoriteDTO>>> ListAsync(
        UserModel caller, CancellationToken cancellationToken = default);

    Task<ServerActionResponse<PromptPublicDTO>> GetAsync(
        string promptId, UserModel caller, CancellationToken cancellationToken = default);

    Task<ServerActionResponse<PromptPublicDTO>> CreateAsync(
        PromptModel draft, UserModel caller, CancellationToken cancellationToken = default);

    Task<ServerActionResponse<PromptPublicDTO>> UpdateAsync(
        string promptId, PromptModel draft, UserModel caller, CancellationToken cancellationToken = default);

    Task<ServerActionResponse<bool>> DeleteAsync(
        string promptId, UserModel caller, CancellationToken cancellationToken = default);

    /// <summary>FR-005–FR-008: mutates ownership fields only, in a single atomic row update.</summary>
    Task<ServerActionResponse<PromptPublicDTO>> TransferOwnershipAsync(
        string promptId, string newOwnerEmail, UserModel caller, CancellationToken cancellationToken = default);

    /// <summary>FR-016: scoped to the caller's own favorites only (SC-007).</summary>
    Task<ServerActionResponse<IReadOnlyList<PromptPublicDTO>>> ListFavoritesAsync(
        UserModel caller, CancellationToken cancellationToken = default);

    /// <summary>FR-016: requires at least read access. Idempotent.</summary>
    Task<ServerActionResponse<bool>> AddFavoriteAsync(
        string promptId, UserModel caller, CancellationToken cancellationToken = default);

    /// <summary>Idempotent; scoped to the caller.</summary>
    Task<ServerActionResponse<bool>> RemoveFavoriteAsync(
        string promptId, UserModel caller, CancellationToken cancellationToken = default);
}
