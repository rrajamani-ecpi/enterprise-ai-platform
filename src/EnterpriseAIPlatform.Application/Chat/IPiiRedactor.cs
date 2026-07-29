using EnterpriseAIPlatform.Domain.Chat;

namespace EnterpriseAIPlatform.Application.Chat;

/// <summary>
/// PII redaction on user-authored text (spec 004 FR-008; constitution Security &amp; Compliance
/// Constraints). Applies only to the model-bound copy — never to persisted history, assistant
/// output, or documents. R1 ships exactly one implementation, the fail-closed regex tier.
/// </summary>
public interface IPiiRedactor
{
    PiiRedactionResult Redact(string userText);
}
