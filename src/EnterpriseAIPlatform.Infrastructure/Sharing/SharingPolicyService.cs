using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.Sharing;
using EnterpriseAIPlatform.Domain.Sharing;
using Microsoft.Extensions.Options;

namespace EnterpriseAIPlatform.Infrastructure.Sharing;

/// <summary>
/// The single implementation of <see cref="ISharingPolicyService"/> (spec 018 FR-002, SC-001).
/// Resolves the current config snapshot on every call and delegates to the pure
/// <see cref="SharingPolicyEvaluator"/> — never a cached-per-user decision.
/// </summary>
public sealed class SharingPolicyService : ISharingPolicyService
{
    private readonly IOptionsSnapshot<RoleSharingPolicyOptions> _rolePolicy;
    private readonly IOptionsSnapshot<GlobalSharingOverrideOptions> _globalOverride;

    public SharingPolicyService(
        IOptionsSnapshot<RoleSharingPolicyOptions> rolePolicy,
        IOptionsSnapshot<GlobalSharingOverrideOptions> globalOverride)
    {
        _rolePolicy = rolePolicy;
        _globalOverride = globalOverride;
    }

    public SharingDecision Evaluate(UserModel caller, ShareTargetRequest request) =>
        SharingPolicyEvaluator.Evaluate(caller.Roles, request, _rolePolicy.Value, _globalOverride.Value);
}
