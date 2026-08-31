using EnterpriseAIPlatform.Domain.Prompts;

namespace EnterpriseAIPlatform.Application.Prompts;

/// <summary>
/// The single validation rule set for prompt writes (spec 016 FR-003/FR-013/FR-014). Enforced in the
/// Application layer so a direct API caller is bound by exactly the same rule as a UI user
/// (Constitution Principle V — schema-enforced, not UI-enforced).
/// </summary>
/// <remarks>
/// A static, DI-free function with no DataAnnotations/FluentValidation dependency, matching the
/// codebase's established convention (<c>PersonaExtensionRules</c>, <c>ConversationRenameRules</c>).
/// </remarks>
public static class PromptValidationRules
{
    public const string NameRequiredMessage = "Prompt name is required.";
    public const string DescriptionRequiredMessage = "Prompt description is required.";

    /// <summary>FR-003: both <c>Name</c> and <c>Description</c> must be present and non-whitespace.</summary>
    public static bool TryValidate(string? name, string? description, out string? error)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            error = NameRequiredMessage;
            return false;
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            error = DescriptionRequiredMessage;
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Convenience overload for an assembled <see cref="PromptModel"/> draft.</summary>
    public static bool TryValidate(PromptModel draft, out string? error) =>
        TryValidate(draft.Name, draft.Description, out error);
}
