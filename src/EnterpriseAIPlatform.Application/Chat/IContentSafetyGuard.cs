using EnterpriseAIPlatform.Domain.Chat;

namespace EnterpriseAIPlatform.Application.Chat;

/// <summary>
/// The Content Safety guardrail at the model boundary (constitution Responsible AI section — not
/// a numbered spec 004 FR, but required regardless). Runs on the original user text, before
/// redaction and before the model call.
/// </summary>
public interface IContentSafetyGuard
{
    Task<ContentSafetyVerdict> CheckAsync(string text, CancellationToken cancellationToken = default);
}
