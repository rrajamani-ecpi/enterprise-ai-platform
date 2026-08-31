using EnterpriseAIPlatform.Application.Prompts;

namespace EnterpriseAIPlatform.UnitTests.Prompts;

/// <summary>Spec 016 FR-003/FR-013/FR-014 — the shared write-path validation rule.</summary>
public class PromptValidationRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void RejectsMissingName(string? name)
    {
        Assert.False(PromptValidationRules.TryValidate(name, "a description", out var error));
        Assert.Equal(PromptValidationRules.NameRequiredMessage, error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void RejectsMissingDescription(string? description)
    {
        Assert.False(PromptValidationRules.TryValidate("a name", description, out var error));
        Assert.Equal(PromptValidationRules.DescriptionRequiredMessage, error);
    }

    [Fact]
    public void AcceptsNonEmptyNameAndDescription()
    {
        Assert.True(PromptValidationRules.TryValidate("Summarize", "Summarize the following text.", out var error));
        Assert.Null(error);
    }

    [Fact]
    public void ReportsNameBeforeDescription_SoTheFirstFailureIsDeterministic()
    {
        Assert.False(PromptValidationRules.TryValidate(null, null, out var error));
        Assert.Equal(PromptValidationRules.NameRequiredMessage, error);
    }
}
