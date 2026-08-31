namespace EnterpriseAIPlatform.Application.Personas;

/// <summary>
/// The single implementation of the "<c>dataProducts</c> required when the <c>DataProduct</c>
/// extension is selected" rule (spec 009 FR-011) — framework-free so it's shared identically by
/// every create/update entry point, never duplicated in a UI-only check (Constitution Principle V),
/// mirroring <c>ConversationRenameRules</c>/<c>MultiChatQuadrantRules</c>'s existing shape.
/// </summary>
public static class PersonaExtensionRules
{
    private const string DataProductExtension = "DataProduct";

    /// <summary>
    /// An empty-string entry counts as no entry (spec Edge Cases) — <paramref name="dataProducts"/>
    /// must contain at least one non-empty, non-whitespace entry when <paramref name="extensions"/>
    /// contains <c>"DataProduct"</c>.
    /// </summary>
    public static bool TryValidate(IReadOnlyList<string> extensions, IReadOnlyList<string> dataProducts, out string? error)
    {
        if (!extensions.Contains(DataProductExtension))
        {
            error = null;
            return true;
        }

        if (dataProducts.Any(d => !string.IsNullOrWhiteSpace(d)))
        {
            error = null;
            return true;
        }

        error = "dataProducts must contain at least one entry when the DataProduct extension is selected.";
        return false;
    }
}
