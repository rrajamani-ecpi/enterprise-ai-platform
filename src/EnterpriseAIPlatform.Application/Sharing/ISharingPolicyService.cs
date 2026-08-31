using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Domain.Sharing;

namespace EnterpriseAIPlatform.Application.Sharing;

/// <summary>
/// The single, server-side sharing-policy decision point (spec 018 FR-002/FR-006/FR-015) that
/// specs 009/012/016 are meant to consume instead of re-implementing their own share-target
/// validity checks (Constitution Principle IV). Computed fresh on every call from the current
/// <see cref="UserModel"/> and current config snapshot — never cached per-user. Exactly one
/// implementation (architecture-tested).
/// </summary>
public interface ISharingPolicyService
{
    SharingDecision Evaluate(UserModel caller, ShareTargetRequest request);
}
