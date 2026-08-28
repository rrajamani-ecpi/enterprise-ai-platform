namespace EnterpriseAIPlatform.Web.Services;

/// <summary>
/// Client-side, per-circuit view of one multi-chat quadrant (spec 024 US4). Mirrors
/// <c>EnterpriseAIPlatform.Domain.Chat.MultiChatQuadrant</c> plus the UI-only fields needed to
/// render it (display name, transcript, per-pane error) — never persisted directly.
/// </summary>
public sealed class ComparePaneViewState
{
    public required int Position { get; set; }

    public string? ModelId { get; set; }

    public string? ModelDisplayName { get; set; }

    public string? ThreadId { get; set; }

    public List<ChatMessageViewState> Messages { get; } = new();

    /// <summary>Set when this pane's own <c>QuadrantEvent.Kind == Error</c> arrives; never affects sibling panes.</summary>
    public string? ErrorMessage { get; set; }
}
