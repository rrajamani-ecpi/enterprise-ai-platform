using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.Chat;
using EnterpriseAIPlatform.Domain.ModelAccess;
using EnterpriseAIPlatform.Infrastructure.Chat;

namespace EnterpriseAIPlatform.Web.Services;

/// <summary>
/// Per-circuit (Scoped) coordinator for the multi-pane comparison screen (spec 024 US4). Composes
/// the same spec 006 Application/Infrastructure services <c>MultiChatEndpoints.cs</c> already
/// calls — no new business logic is introduced, only a second caller of the existing one
/// (Constitution Principle IV). Quadrant floor/cap and model-allow-list enforcement remain
/// entirely in <see cref="IMultiChatSessionStore"/>/<c>MultiChatQuadrantRules</c> — this class
/// never re-implements either.
/// </summary>
public sealed class CompareSessionState
{
    private readonly IIdentityHasher _identityHasher;
    private readonly IMultiChatSessionStore _sessionStore;
    private readonly MultiChatDispatcher _dispatcher;
    private readonly IModelAccessService _modelAccessService;
    private readonly IChatMessageStore _messageStore;

    private string? _partitionKey;
    private MultiChatSession? _session;

    public CompareSessionState(
        ICurrentUserAccessor currentUserAccessor,
        IIdentityHasher identityHasher,
        IMultiChatSessionStore sessionStore,
        MultiChatDispatcher dispatcher,
        IModelAccessService modelAccessService,
        IChatMessageStore messageStore)
    {
        _identityHasher = identityHasher;
        _sessionStore = sessionStore;
        _dispatcher = dispatcher;
        _modelAccessService = modelAccessService;
        _messageStore = messageStore;

        // Cached once here for the same reason ChatComposerState caches it: IHttpContextAccessor
        // (which ICurrentUserAccessor depends on) is only populated for the initial request that
        // starts this circuit, not for later UI-driven calls over the persistent SignalR connection.
        var callerResult = currentUserAccessor.GetCurrentUser();
        CurrentUser = callerResult.IsSuccess ? callerResult.Response : null;
    }

    public UserModel? CurrentUser { get; }

    public List<ComparePaneViewState> Panes { get; } = new();

    /// <summary>The caller's allow-listed models (spec 014) — the only options every pane's model picker may offer.</summary>
    public IReadOnlyList<ModelConfigDocument> AvailableModels { get; private set; } = Array.Empty<ModelConfigDocument>();

    public string ComposerText { get; set; } = string.Empty;

    public bool IsSending { get; private set; }

    public string? PaneErrorMessage { get; private set; }

    /// <summary>Raised after every state mutation, so the component can call StateHasChanged().</summary>
    public event Action? OnChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentUser is null)
        {
            return;
        }

        _partitionKey = _identityHasher.ForEmail(CurrentUser.Email).Value;

        var modelsResult = await _modelAccessService.GetAvailableModelsAsync(CurrentUser, cancellationToken);
        AvailableModels = modelsResult.IsSuccess && modelsResult.Response is not null
            ? modelsResult.Response
            : Array.Empty<ModelConfigDocument>();

        _session = await _sessionStore.GetOrCreateAsync(_partitionKey, CurrentUser.Email, cancellationToken);
        await RefreshPanesAsync(cancellationToken);
        NotifyChanged();
    }

    /// <summary>Spec 006 FR-005 — refused at the 4-pane cap; <see cref="PaneErrorMessage"/> carries the reason.</summary>
    public async Task AddPaneAsync(CancellationToken cancellationToken = default)
    {
        if (_partitionKey is null)
        {
            return;
        }

        var result = await _sessionStore.AddQuadrantAsync(_partitionKey, cancellationToken);
        if (result.IsSuccess && result.Response is not null)
        {
            PaneErrorMessage = null;
            _session = result.Response;
            await RefreshPanesAsync(cancellationToken);
        }
        else
        {
            PaneErrorMessage = "A comparison session may not exceed 4 panes.";
        }

        NotifyChanged();
    }

    /// <summary>Spec 006 FR-004 — at the 2-pane floor, the store clears the assignment instead of removing the pane.</summary>
    public async Task RemovePaneAsync(CancellationToken cancellationToken = default)
    {
        if (_partitionKey is null)
        {
            return;
        }

        _session = await _sessionStore.RemoveQuadrantAsync(_partitionKey, cancellationToken);
        PaneErrorMessage = null;
        await RefreshPanesAsync(cancellationToken);
        NotifyChanged();
    }

    /// <summary><paramref name="modelId"/> MUST come from <see cref="AvailableModels"/> — the picker never offers anything else, so no separate validation call is needed (research.md).</summary>
    public async Task AssignModelAsync(int position, string modelId, CancellationToken cancellationToken = default)
    {
        if (_partitionKey is null)
        {
            return;
        }

        _session = await _sessionStore.AssignModelAsync(_partitionKey, position, modelId, cancellationToken);
        await RefreshPanesAsync(cancellationToken);
        NotifyChanged();
    }

    /// <summary>Spec 006 FR-009 — one message dispatched to every assigned pane; a pane's error never blocks another's completion.</summary>
    public async Task SendToAllAsync(CancellationToken cancellationToken = default)
    {
        if (IsSending || string.IsNullOrWhiteSpace(ComposerText) || CurrentUser is null || _session is null || _partitionKey is null)
        {
            return;
        }

        var text = ComposerText;
        ComposerText = string.Empty;
        IsSending = true;

        foreach (var pane in Panes.Where(p => p.ModelId is not null))
        {
            pane.ErrorMessage = null;
            pane.Messages.Add(new ChatMessageViewState { Role = "assistant", IsComplete = false });
        }

        NotifyChanged();

        await foreach (var evt in _dispatcher.DispatchAsync(CurrentUser, _session, text, cancellationToken))
        {
            var pane = Panes.FirstOrDefault(p => p.Position == evt.Position);
            if (pane is null || pane.Messages.Count == 0)
            {
                continue;
            }

            var message = pane.Messages[^1];
            switch (evt.Kind)
            {
                case QuadrantEventKind.Chunk:
                    message.Content += evt.Content;
                    break;
                case QuadrantEventKind.Error:
                    pane.ErrorMessage = evt.Content;
                    message.IsComplete = true;
                    break;
                case QuadrantEventKind.Done:
                    message.IsComplete = true;
                    break;
            }

            NotifyChanged();
        }

        IsSending = false;

        // The dispatcher persists any on-demand-created ThreadId via SetQuadrantThreadAsync, but
        // never updates this in-memory _session — re-fetch so a follow-up send targets the same
        // per-pane thread instead of creating a new one every time.
        _session = await _sessionStore.GetOrCreateAsync(_partitionKey, CurrentUser.Email, cancellationToken);
        foreach (var quadrant in _session.Quadrants)
        {
            var pane = Panes.FirstOrDefault(p => p.Position == quadrant.Position);
            if (pane is not null)
            {
                pane.ThreadId = quadrant.ThreadId;
            }
        }

        NotifyChanged();
    }

    private async Task RefreshPanesAsync(CancellationToken cancellationToken)
    {
        if (_session is null || _partitionKey is null)
        {
            return;
        }

        Panes.Clear();
        foreach (var quadrant in _session.Quadrants.OrderBy(q => q.Position))
        {
            var pane = new ComparePaneViewState
            {
                Position = quadrant.Position,
                ModelId = quadrant.ModelId,
                ModelDisplayName = AvailableModels.FirstOrDefault(m => m.Id == quadrant.ModelId)?.DisplayName,
                ThreadId = quadrant.ThreadId,
            };

            if (quadrant.ThreadId is not null)
            {
                var history = await _messageStore.ListByThreadAsync(quadrant.ThreadId, _partitionKey, cancellationToken);
                pane.Messages.AddRange(history.Select(m => new ChatMessageViewState
                {
                    Role = m.Role == ChatMessageRole.User ? "user" : "assistant",
                    Content = m.Content,
                    IsComplete = true,
                }));
            }

            Panes.Add(pane);
        }
    }

    private void NotifyChanged() => OnChanged?.Invoke();
}
