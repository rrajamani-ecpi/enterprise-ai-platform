using EnterpriseAIPlatform.Application.Personas;

namespace EnterpriseAIPlatform.UnitTests.Personas;

/// <summary>Spec 009 US4 / FR-011 / SC-007: the "dataProducts required when DataProduct extension is selected" rule.</summary>
public class PersonaExtensionRulesTests
{
    [Fact]
    public void Rejects_DataProductExtension_WithEmptyDataProducts()
    {
        var isValid = PersonaExtensionRules.TryValidate(new[] { "DataProduct" }, Array.Empty<string>(), out var error);

        Assert.False(isValid);
        Assert.NotNull(error);
    }

    [Fact]
    public void Rejects_DataProductExtension_WithAbsentDataProducts()
    {
        var isValid = PersonaExtensionRules.TryValidate(new[] { "DataProduct" }, new List<string>(), out _);

        Assert.False(isValid);
    }

    [Fact]
    public void Rejects_DataProductExtension_WithOnlyEmptyStringEntries()
    {
        // Spec Edge Cases: an empty-string-array entry counts as no entry.
        var isValid = PersonaExtensionRules.TryValidate(new[] { "DataProduct" }, new[] { "", "   " }, out _);

        Assert.False(isValid);
    }

    [Fact]
    public void Accepts_DataProductExtension_WithAtLeastOneEntry()
    {
        var isValid = PersonaExtensionRules.TryValidate(new[] { "DataProduct" }, new[] { "product-1" }, out var error);

        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void Accepts_NoDataProductExtension_WithEmptyDataProducts()
    {
        var isValid = PersonaExtensionRules.TryValidate(Array.Empty<string>(), Array.Empty<string>(), out var error);

        Assert.True(isValid);
        Assert.Null(error);
    }
}
