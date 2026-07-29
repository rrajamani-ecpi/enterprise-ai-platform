using System.Text.RegularExpressions;
using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Domain.Chat;

namespace EnterpriseAIPlatform.Infrastructure.Redaction;

/// <summary>
/// The single, R1-only implementation of <see cref="IPiiRedactor"/> (spec 004 FR-008; constitution
/// Security &amp; Compliance Constraints). This IS the constitution's fail-closed regex tier —
/// there is no ML-based tier in R1 (no Azure PII-detection service is provisioned in this
/// environment); this is a documented scope decision (plan.md D6), not a silent gap.
/// </summary>
public sealed partial class RegexPiiRedactor : IPiiRedactor
{
    public PiiRedactionResult Redact(string userText)
    {
        var count = 0;

        string Replace(string input, Regex pattern, string placeholder)
        {
            return pattern.Replace(input, _ =>
            {
                count++;
                return placeholder;
            });
        }

        var redacted = userText;
        redacted = Replace(redacted, EmailPattern(), "[REDACTED_EMAIL]");
        redacted = Replace(redacted, PhonePattern(), "[REDACTED_PHONE]");
        redacted = Replace(redacted, SsnPattern(), "[REDACTED_SSN]");
        redacted = Replace(redacted, CreditCardPattern(), "[REDACTED_CARD]");

        return new PiiRedactionResult(redacted, count);
    }

    [GeneratedRegex(@"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<!\d)(\+?1[\s.\-]?)?\(?\d{3}\)?[\s.\-]?\d{3}[\s.\-]?\d{4}(?!\d)")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"(?<!\d)\d{3}-\d{2}-\d{4}(?!\d)")]
    private static partial Regex SsnPattern();

    [GeneratedRegex(@"(?<!\d)(?:\d[ -]?){13,16}(?!\d)")]
    private static partial Regex CreditCardPattern();
}
