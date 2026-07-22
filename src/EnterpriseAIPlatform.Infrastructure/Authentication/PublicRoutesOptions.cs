namespace EnterpriseAIPlatform.Infrastructure.Authentication;

/// <summary>
/// Explicit, closed-by-default public-route allow-list (spec 002 FR-011). Every path NOT
/// listed here requires an authenticated session via the fallback authorization policy.
/// </summary>
public sealed class PublicRoutesOptions
{
    public const string SectionName = "PublicRoutes";

    public List<string> Paths { get; init; } = new();
}
