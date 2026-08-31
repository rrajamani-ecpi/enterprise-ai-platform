using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Domain.Prompts;
using EnterpriseAIPlatform.Domain.Sharing;

namespace EnterpriseAIPlatform.Application.Prompts;

/// <summary>
/// The single implementation of spec 016's prompt access gate (FR-001/FR-002/FR-006;
/// Constitution Principle IV) — framework-free so the full role x ownership x collaborator x
/// share-target matrix (SC-004) is unit-testable without any infrastructure, mirroring spec 009's
/// <c>PersonaAccessEvaluator</c> and spec 018's <c>SharingPolicyEvaluator</c>.
/// </summary>
/// <remarks>
/// Takes <paramref name="callerPartitionKey"/> as a precomputed value (via
/// <c>IIdentityHasher.ForEmail</c>, spec 002) rather than hashing internally — keeps this class
/// DI-free without duplicating the one canonical hashing implementation.
/// </remarks>
public static class PromptAccessEvaluator
{
    /// <summary>
    /// FR-001 + FR-002: write access (admin / owner / collaborator) implies read, and read is
    /// additionally granted to anyone the prompt is shared with — by hashed individual identity or
    /// by a group token the caller carries on <see cref="UserModel.GroupTokens"/>.
    /// </summary>
    public static bool CanRead(PromptModel prompt, UserModel caller, string callerPartitionKey)
    {
        if (CanWrite(prompt, caller, callerPartitionKey))
        {
            return true;
        }

        foreach (var target in prompt.SharedWith)
        {
            var matches = target.Type switch
            {
                ShareTargetType.Individual =>
                    target.IdentityPartitionKey is { Length: > 0 } identity &&
                    string.Equals(identity, callerPartitionKey, StringComparison.Ordinal),

                ShareTargetType.Group =>
                    target.GroupToken is { Length: > 0 } token &&
                    caller.GroupTokens.Contains(token, StringComparer.Ordinal),

                _ => false,
            };

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// FR-001: write access is granted only to admins, the owner, or a designated collaborator.
    /// Share targets are deliberately excluded — a share grant of either kind is read-only and never
    /// confers write (FR-002).
    /// </summary>
    public static bool CanWrite(PromptModel prompt, UserModel caller, string callerPartitionKey) =>
        caller.IsAdmin
        || string.Equals(prompt.OwnerPartitionKey, callerPartitionKey, StringComparison.Ordinal)
        || prompt.CollaboratorPartitionKeys.Contains(callerPartitionKey, StringComparer.Ordinal);

    /// <summary>
    /// FR-006: only the owner or an admin may transfer ownership. Deliberately narrower than
    /// <see cref="CanWrite"/> — a collaborator may edit a prompt but may not give it away — so this
    /// is not an alias for the write gate.
    /// </summary>
    public static bool CanTransfer(PromptModel prompt, UserModel caller, string callerPartitionKey) =>
        caller.IsAdmin
        || string.Equals(prompt.OwnerPartitionKey, callerPartitionKey, StringComparison.Ordinal);
}
