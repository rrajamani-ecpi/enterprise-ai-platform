namespace EnterpriseAIPlatform.Web.Services;

/// <summary>
/// Client-side, per-circuit transcript entry for the chat home screen (spec 024 US1/US2).
/// Never persisted — the source of truth is <c>ChatMessageModel</c>, written by <c>IChatPipeline</c>.
/// </summary>
public sealed class ChatMessageViewState
{
    public required string Role { get; init; }

    public string Content { get; set; } = string.Empty;

    /// <summary>Set when the stream ended via cancellation/exception rather than normal completion (FR-012).</summary>
    public bool IsInterrupted { get; set; }

    /// <summary>False while an assistant reply is still receiving chunks.</summary>
    public bool IsComplete { get; set; } = true;
}
