using EnterpriseAIPlatform.Infrastructure.Redaction;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>Spec 004 FR-008 / constitution Security &amp; Compliance Constraints — R1's sole (fail-closed) redaction tier.</summary>
public class RegexPiiRedactorTests
{
    private readonly RegexPiiRedactor _sut = new();

    [Fact]
    public void Redact_RemovesEmailAddress()
    {
        var result = _sut.Redact("Reach me at alice.smith@contoso.com for details.");

        Assert.DoesNotContain("alice.smith@contoso.com", result.RedactedText);
        Assert.Contains("[REDACTED_EMAIL]", result.RedactedText);
        Assert.Equal(1, result.RedactionCount);
    }

    [Theory]
    [InlineData("Call me at (555) 123-4567.")]
    [InlineData("Call me at 555-123-4567.")]
    [InlineData("Call me at 555.123.4567.")]
    public void Redact_RemovesPhoneNumber(string input)
    {
        var result = _sut.Redact(input);

        Assert.Contains("[REDACTED_PHONE]", result.RedactedText);
        Assert.DoesNotContain("123-4567", result.RedactedText);
    }

    [Fact]
    public void Redact_RemovesSsnShapedSequence()
    {
        var result = _sut.Redact("My SSN is 123-45-6789.");

        Assert.Contains("[REDACTED_SSN]", result.RedactedText);
        Assert.DoesNotContain("123-45-6789", result.RedactedText);
    }

    [Fact]
    public void Redact_RemovesCreditCardShapedSequence()
    {
        var result = _sut.Redact("Card number: 4111111111111111 please.");

        Assert.Contains("[REDACTED_CARD]", result.RedactedText);
        Assert.DoesNotContain("4111111111111111", result.RedactedText);
    }

    [Fact]
    public void Redact_LeavesOrdinaryTextUnchanged()
    {
        const string text = "What's the weather like in Boston today?";

        var result = _sut.Redact(text);

        Assert.Equal(text, result.RedactedText);
        Assert.Equal(0, result.RedactionCount);
    }

    [Fact]
    public void Redact_CountsMultipleRedactions()
    {
        var result = _sut.Redact("Email me at a@b.com or call 555-123-4567.");

        Assert.Equal(2, result.RedactionCount);
    }
}
