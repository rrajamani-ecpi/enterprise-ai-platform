using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Domain.Chat;

namespace EnterpriseAIPlatform.Web.Services;

/// <summary>
/// Per-circuit (Scoped) coordinator for the conversation sidebar (spec 024 US3). A sibling of
/// <see cref="ChatComposerState"/>, not a merge into it — this owns the list/rename flow, while
/// <see cref="ChatComposerState"/> remains the single owner of "what thread is currently active."
/// </summary>
public sealed class ConversationListState
{
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly IIdentityHasher _identityHasher;
    private readonly IChatThreadStore _threadStore;

    public ConversationListState(
        ICurrentUserAccessor currentUserAccessor, IIdentityHasher identityHasher, IChatThreadStore threadStore)
    {
        _currentUserAccessor = currentUserAccessor;
        _identityHasher = identityHasher;
        _threadStore = threadStore;
    }

    public List<ChatThreadModel> Conversations { get; } = new();

    public bool IsLoading { get; private set; }

    public string? RenameErrorMessage { get; private set; }

    /// <summary>
    /// Set when <see cref="LoadConversationsAsync"/> fails (e.g. a transient store outage). A
    /// degraded sidebar — never a crashed page: the active conversation's composer/transcript must
    /// keep working even if the list can't load (Constitution Principle III — fail loud, but don't
    /// let one surface's failure cascade into another's).
    /// </summary>
    public string? LoadErrorMessage { get; private set; }

    /// <summary>Raised after every state mutation, so the component can call StateHasChanged().</summary>
    public event Action? OnChanged;

    /// <summary>Spec 024 US3 FR-005 — the caller's own conversations, most-recently-active first.</summary>
    public async Task LoadConversationsAsync(CancellationToken cancellationToken = default)
    {
        var callerResult = _currentUserAccessor.GetCurrentUser();
        if (!callerResult.IsSuccess || callerResult.Response is null)
        {
            return;
        }

        IsLoading = true;
        LoadErrorMessage = null;
        NotifyChanged();

        try
        {
            var partitionKey = _identityHasher.ForEmail(callerResult.Response.Email).Value;
            var conversations = await _threadStore.ListByOwnerAsync(partitionKey, cancellationToken);

            Conversations.Clear();
            Conversations.AddRange(conversations);
        }
        catch (Exception)
        {
            LoadErrorMessage = "Couldn't load your conversations. Please try reloading the page.";
        }
        finally
        {
            IsLoading = false;
            NotifyChanged();
        }
    }

    /// <summary>Spec 024 US3 FR-007 — rejects empty/whitespace names, leaving the prior name in effect.</summary>
    public async Task RenameAsync(string threadId, string newName, CancellationToken cancellationToken = default)
    {
        var callerResult = _currentUserAccessor.GetCurrentUser();
        if (!callerResult.IsSuccess || callerResult.Response is null)
        {
            return;
        }

        var partitionKey = _identityHasher.ForEmail(callerResult.Response.Email).Value;
        var result = await _threadStore.RenameAsync(threadId, partitionKey, newName, cancellationToken);

        if (result.Status == ResponseStatus.OK)
        {
            RenameErrorMessage = null;
            var index = Conversations.FindIndex(c => c.Id == threadId);
            if (index >= 0)
            {
                Conversations[index] = result.Response!;
            }
        }
        else
        {
            RenameErrorMessage = "A conversation name cannot be empty.";
        }

        NotifyChanged();
    }

    private void NotifyChanged() => OnChanged?.Invoke();
}
