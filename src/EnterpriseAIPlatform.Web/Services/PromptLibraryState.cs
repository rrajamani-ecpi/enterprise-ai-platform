using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.Prompts;
using EnterpriseAIPlatform.Domain.Prompts;
using EnterpriseAIPlatform.Infrastructure.Prompts;

namespace EnterpriseAIPlatform.Web.Services;

/// <summary>
/// Per-circuit (Scoped) coordinator for the prompt library UI (spec 016 US1–US6). Calls the same
/// <see cref="IPromptService"/> / <see cref="IPromptGenerationService"/> seams the endpoints call —
/// no prompt logic lives here, and in particular no access decision (Constitution Principle IV).
/// The <c>Can*</c> members below are presentation hints for hiding controls the caller can't use;
/// the server-side gate in <c>PromptService</c> remains the enforcement.
/// </summary>
public sealed class PromptLibraryState
{
    private readonly IPromptService _prompts;
    private readonly IPromptGenerationService _generator;
    private readonly IIdentityHasher _identityHasher;

    public PromptLibraryState(
        ICurrentUserAccessor currentUserAccessor,
        IPromptService prompts,
        IPromptGenerationService generator,
        IIdentityHasher identityHasher)
    {
        _prompts = prompts;
        _generator = generator;
        _identityHasher = identityHasher;

        // Resolved once, for the same reason ChatComposerState does it: HttpContext exists only for
        // the request that starts the circuit, so re-resolving on a later button click would
        // spuriously report "no session".
        var callerResult = currentUserAccessor.GetCurrentUser();
        CurrentUser = callerResult.IsSuccess ? callerResult.Response : null;
        CallerPartitionKey = CurrentUser is null ? string.Empty : _identityHasher.ForEmail(CurrentUser.Email).Value;
    }

    public UserModel? CurrentUser { get; }

    public string CallerPartitionKey { get; }

    public List<PromptWithFavoriteDTO> Prompts { get; } = new();

    public bool ShowFavoritesOnly { get; private set; }

    public bool IsBusy { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// FR-004 — set when a write loses to a concurrent one. Kept separate from
    /// <see cref="ErrorMessage"/> so the UI can render the specific "reload and retry" guidance
    /// rather than folding a recoverable conflict into a generic failure.
    /// </summary>
    public bool HasConcurrencyConflict { get; private set; }

    public event Action? OnChanged;

    public IReadOnlyList<PromptWithFavoriteDTO> VisiblePrompts =>
        ShowFavoritesOnly ? Prompts.Where(p => p.IsFavorite).ToList() : Prompts;

    /// <summary>UI hint only — mirrors <see cref="PromptAccessEvaluator.CanWrite"/> without duplicating its rules.</summary>
    public bool CanWrite(PromptPublicDTO prompt) =>
        CurrentUser is not null && PromptAccessEvaluator.CanWrite(ToModel(prompt), CurrentUser, CallerPartitionKey);

    /// <summary>UI hint only — mirrors <see cref="PromptAccessEvaluator.CanTransfer"/>.</summary>
    public bool CanTransfer(PromptPublicDTO prompt) =>
        CurrentUser is not null && PromptAccessEvaluator.CanTransfer(ToModel(prompt), CurrentUser, CallerPartitionKey);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentUser is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var result = await _prompts.ListAsync(CurrentUser, cancellationToken);
            if (Apply(result))
            {
                Prompts.Clear();
                Prompts.AddRange(result.Response!);
            }
        });
    }

    public Task<bool> CreateAsync(string name, string description, CancellationToken cancellationToken = default) =>
        WriteAsync(() => _prompts.CreateAsync(BuildDraft(name, description), CurrentUser!, cancellationToken), cancellationToken);

    public Task<bool> UpdateAsync(string promptId, string name, string description, CancellationToken cancellationToken = default) =>
        WriteAsync(() => _prompts.UpdateAsync(promptId, BuildDraft(name, description), CurrentUser!, cancellationToken), cancellationToken);

    public Task<bool> TransferOwnershipAsync(string promptId, string newOwnerEmail, CancellationToken cancellationToken = default) =>
        WriteAsync(() => _prompts.TransferOwnershipAsync(promptId, newOwnerEmail, CurrentUser!, cancellationToken), cancellationToken);

    public async Task<bool> DeleteAsync(string promptId, CancellationToken cancellationToken = default)
    {
        if (CurrentUser is null)
        {
            return false;
        }

        var succeeded = false;
        await RunAsync(async () =>
        {
            var result = await _prompts.DeleteAsync(promptId, CurrentUser, cancellationToken);
            succeeded = Apply(result);
        });

        if (succeeded)
        {
            await LoadAsync(cancellationToken);
        }

        return succeeded;
    }

    public async Task ToggleFavoriteAsync(string promptId, bool isFavorite, CancellationToken cancellationToken = default)
    {
        if (CurrentUser is null)
        {
            return;
        }

        var succeeded = false;
        await RunAsync(async () =>
        {
            var result = isFavorite
                ? await _prompts.RemoveFavoriteAsync(promptId, CurrentUser, cancellationToken)
                : await _prompts.AddFavoriteAsync(promptId, CurrentUser, cancellationToken);
            succeeded = Apply(result);
        });

        if (succeeded)
        {
            await LoadAsync(cancellationToken);
        }
    }

    public void SetFavoritesFilter(bool favoritesOnly)
    {
        ShowFavoritesOnly = favoritesOnly;
        NotifyChanged();
    }

    /// <summary>
    /// FR-010/FR-011 — returns the generated text, or <c>null</c> with <see cref="ErrorMessage"/>
    /// populated. It never returns the error text as if it were generated content (Principle III).
    /// </summary>
    public async Task<string?> GenerateDescriptionAsync(string intent, CancellationToken cancellationToken = default)
    {
        if (CurrentUser is null)
        {
            return null;
        }

        string? generated = null;
        await RunAsync(async () =>
        {
            var result = await _generator.GenerateAsync(new PromptGenerationRequest(intent), CurrentUser, cancellationToken);
            if (Apply(result))
            {
                generated = result.Response!.GeneratedText;
            }
        });

        return generated;
    }

    private async Task<bool> WriteAsync<T>(
        Func<Task<ServerActionResponse<T>>> operation, CancellationToken cancellationToken)
    {
        if (CurrentUser is null)
        {
            return false;
        }

        var succeeded = false;
        await RunAsync(async () => succeeded = Apply(await operation()));

        if (succeeded)
        {
            await LoadAsync(cancellationToken);
        }

        return succeeded;
    }

    private async Task RunAsync(Func<Task> operation)
    {
        IsBusy = true;
        ErrorMessage = null;
        HasConcurrencyConflict = false;
        NotifyChanged();

        try
        {
            await operation();
        }
        finally
        {
            IsBusy = false;
            NotifyChanged();
        }
    }

    /// <summary>
    /// Translates one service result into UI state. Failures are surfaced verbatim from the service
    /// so the UI never invents a friendlier message that would contradict the non-revealing error
    /// contract (FR-009).
    /// </summary>
    private bool Apply<T>(ServerActionResponse<T> result)
    {
        if (result.Status == ResponseStatus.OK)
        {
            return true;
        }

        var message = result.Errors.Count > 0 ? result.Errors[0].Message : "The request could not be completed.";
        HasConcurrencyConflict = message == PromptService.ConcurrencyConflictMessage;
        ErrorMessage = message;
        return false;
    }

    private PromptModel BuildDraft(string name, string description) => new()
    {
        // Owner fields are placeholders; PromptService always sets them from the authenticated
        // caller, never from anything the UI supplies (FR-005).
        Id = string.Empty,
        OwnerUserId = string.Empty,
        OwnerPartitionKey = string.Empty,
        Name = name,
        Description = description,
    };

    /// <summary>
    /// Rehydrates the fields <see cref="PromptAccessEvaluator"/> reads. The DTO deliberately omits
    /// <c>OwnerPartitionKey</c>, so it is recomputed here from the owner's email via the one
    /// canonical hasher rather than being added to the wire contract.
    /// </summary>
    private PromptModel ToModel(PromptPublicDTO prompt) => new()
    {
        Id = prompt.Id,
        OwnerUserId = prompt.OwnerUserId,
        OwnerPartitionKey = _identityHasher.ForEmail(prompt.OwnerUserId).Value,
        Name = prompt.Name,
        Description = prompt.Description,
        CollaboratorPartitionKeys = prompt.CollaboratorPartitionKeys.ToList(),
        SharedWith = prompt.SharedWith.ToList(),
    };

    private void NotifyChanged() => OnChanged?.Invoke();
}
