using EnterpriseAIPlatform.Infrastructure.Identity;
using Xunit;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>Spec 002 US6 / FR-014 / SC-008: deterministic identity hashing, never raw.</summary>
public class IdentityHasherTests
{
    private readonly IdentityHasher _hasher = new();

    [Fact]
    public void ForEmail_ProducesHash_NotRawEmail()
    {
        const string email = "alice@contoso.com";

        var key = _hasher.ForEmail(email);

        Assert.NotEqual(email, key.Value);
        Assert.DoesNotContain("@", key.Value);
        Assert.Equal(64, key.Value.Length); // SHA-256 hex
    }

    [Theory]
    [InlineData("Alice@Contoso.com", "alice@contoso.com")]
    [InlineData("  alice@contoso.com  ", "alice@contoso.com")]
    [InlineData("ALICE@CONTOSO.COM", "alice@contoso.com")]
    public void ForEmail_NormalizesCasingAndWhitespace(string variant, string canonical)
    {
        var variantKey = _hasher.ForEmail(variant);
        var canonicalKey = _hasher.ForEmail(canonical);

        Assert.Equal(canonicalKey, variantKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ForEmail_RejectsMissingEmail(string? email)
    {
        Assert.ThrowsAny<ArgumentException>(() => _hasher.ForEmail(email!));
    }
}
