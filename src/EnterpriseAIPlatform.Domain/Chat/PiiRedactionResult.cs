namespace EnterpriseAIPlatform.Domain.Chat;

/// <summary>The computed (never persisted) result of PII redaction (spec 004 FR-008).</summary>
public sealed record PiiRedactionResult(string RedactedText, int RedactionCount);
