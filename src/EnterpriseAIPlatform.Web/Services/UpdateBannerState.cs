using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.Support;

namespace EnterpriseAIPlatform.Web.Services;

/// <summary>
/// Per-circuit (Scoped) coordinator for the global version-update banner (spec 024 US5). Composes
/// the same spec 017 Application services <c>SupportEndpoints.cs</c> already calls — no new
/// business logic, only a second caller of the existing one (Constitution Principle IV).
/// </summary>
public sealed class UpdateBannerState
{
    private readonly IIdentityHasher _identityHasher;
    private readonly IChangelogReader _changelogReader;
    private readonly IVersionAcknowledgmentStore _acknowledgmentStore;

    public UpdateBannerState(
        ICurrentUserAccessor currentUserAccessor,
        IIdentityHasher identityHasher,
        IChangelogReader changelogReader,
        IVersionAcknowledgmentStore acknowledgmentStore)
    {
        _identityHasher = identityHasher;
        _changelogReader = changelogReader;
        _acknowledgmentStore = acknowledgmentStore;

        var callerResult = currentUserAccessor.GetCurrentUser();
        CurrentUser = callerResult.IsSuccess ? callerResult.Response : null;
    }

    public UserModel? CurrentUser { get; }

    public bool ShowAlert { get; private set; }

    public string? LatestVersion { get; private set; }

    public bool IsDismissed { get; private set; }

    /// <summary>Raised after every state mutation, so the component can call StateHasChanged().</summary>
    public event Action? OnChanged;

    /// <summary>
    /// Fails open (banner simply doesn't show) rather than propagating — this is a documented,
    /// non-critical dependency (Constitution Principle III's carve-out: "optional dependencies...
    /// MAY fail open without breaking core chat, precisely because that policy is explicit and
    /// documented, not silent"). Unlike <see cref="Application.Support.IChangelogReader"/> (which
    /// never throws by contract), <see cref="Application.Support.IVersionAcknowledgmentStore"/> is
    /// Cosmos-backed and can throw — and because this banner is rendered from <c>MainLayout</c> on
    /// every page, an unhandled failure here would take down every page in the app, not just the
    /// changelog surface, which is exactly the failure this guard exists to prevent.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentUser is null)
        {
            return;
        }

        try
        {
            var entries = await _changelogReader.GetEntriesAsync(cancellationToken);
            var latest = entries.FirstOrDefault();
            var partitionKey = _identityHasher.ForEmail(CurrentUser.Email).Value;
            var acknowledgment = await _acknowledgmentStore.GetAsync(partitionKey, cancellationToken);

            LatestVersion = latest?.Version.ToString();
            ShowAlert = AlertWindowEvaluator.ShouldShowAlert(latest, acknowledgment, DateTimeOffset.UtcNow);
        }
        catch (Exception)
        {
            ShowAlert = false;
        }

        NotifyChanged();
    }

    /// <summary>Optimistic dismiss — reverted if the persist fails (Constitution Principle III; mirrors <c>SupportEndpoints.cs</c>'s own "never look like success" comment on this same write).</summary>
    public async Task DismissAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentUser is null || LatestVersion is null)
        {
            return;
        }

        IsDismissed = true;
        NotifyChanged();

        try
        {
            var partitionKey = _identityHasher.ForEmail(CurrentUser.Email).Value;
            await _acknowledgmentStore.SetAsync(partitionKey, LatestVersion, cancellationToken);
        }
        catch (Exception)
        {
            IsDismissed = false;
            NotifyChanged();
        }
    }

    private void NotifyChanged() => OnChanged?.Invoke();
}
