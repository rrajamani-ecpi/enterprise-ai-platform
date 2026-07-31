namespace EnterpriseAIPlatform.Domain.Support;

/// <summary>
/// A feedback submission (spec 017 Key Entities) — verified against the caller's own thread,
/// forwarded externally, and never persisted in this application's own store (FR-009).
/// </summary>
public sealed record FeedbackSubmission(string ThreadId, string Content);
