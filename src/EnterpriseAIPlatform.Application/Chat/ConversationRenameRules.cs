namespace EnterpriseAIPlatform.Application.Chat;

/// <summary>
/// The single implementation of conversation rename validation (spec 024 US3 FR-007) —
/// framework-free so it's shared identically by <see cref="IChatThreadStore.RenameAsync"/> and any
/// caller of it (Blazor state or a direct API request), never duplicated in a UI-only check
/// (Constitution Principle V).
/// </summary>
public static class ConversationRenameRules
{
    /// <summary>Trims <paramref name="candidate"/> into <paramref name="trimmed"/>; returns false if the result is empty.</summary>
    public static bool TryValidate(string? candidate, out string trimmed)
    {
        trimmed = candidate?.Trim() ?? string.Empty;
        return trimmed.Length > 0;
    }
}
