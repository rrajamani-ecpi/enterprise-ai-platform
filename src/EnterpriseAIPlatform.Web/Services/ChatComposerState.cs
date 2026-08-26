using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.Chat;

namespace EnterpriseAIPlatform.Web.Services;

/// <summary>
/// Per-circuit (Scoped) coordinator for the chat home screen (spec 024 US1/US2). Composes the
/// same spec 004/014 Application services <c>ChatEndpoints.cs</c> already calls — no new
/// business logic is introduced, only a second caller of the existing one (Constitution
/// Principle IV).
/// </summary>
public sealed class ChatComposerState
{
    private readonly IIdentityHasher _identityHasher;
    private readonly IChatThreadStore _threadStore;
    private readonly IChatMessageStore _messageStore;
    private readonly IChatPipeline _chatPipeline;
    private readonly IModelAccessService _modelAccessService;

    public ChatComposerState(
        ICurrentUserAccessor currentUserAccessor,
        IIdentityHasher identityHasher,
        IChatThreadStore threadStore,
        IChatMessageStore messageStore,
        IChatPipeline chatPipeline,
        IModelAccessService modelAccessService)
    {
        _identityHasher = identityHasher;
        _threadStore = threadStore;
        _messageStore = messageStore;
        _chatPipeline = chatPipeline;
        _modelAccessService = modelAccessService;

        // Resolved once here, not per-send: IHttpContextAccessor.HttpContext (which
        // ICurrentUserAccessor depends on) is only populated for the initial request that starts
        // this circuit — later UI-driven calls (e.g. a button click) run over the persistent
        // SignalR connection with no HttpContext, so re-resolving later would spuriously report
        // "no session". This service is Scoped per circuit, so caching here is safe for its
        // whole lifetime.
        var callerResult = currentUserAccessor.GetCurrentUser();
        CurrentUser = callerResult.IsSuccess ? callerResult.Response : null;
    }

    public UserModel? CurrentUser { get; }

    public string? ThreadId { get; private set; }

    public string? ModelId { get; private set; }

    public List<ChatMessageViewState> Messages { get; } = new();

    public string ComposerText { get; set; } = string.Empty;

    public bool IsStreaming { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>Spec 024 US3 FR-013 — set by <see cref="SwitchToAsync"/> when the given thread doesn't resolve to one of the caller's own conversations.</summary>
    public bool IsNotFound { get; private set; }

    /// <summary>Raised after every state mutation that happens mid-stream, so the component can call StateHasChanged().</summary>
    public event Action? OnChanged;

    /// <summary>Spec 024 US3 FR-006 — switches to an existing conversation, loading its full history before anything is sent. Returns false if the thread doesn't resolve (see <see cref="IsNotFound"/>).</summary>
    public async Task<bool> SwitchToAsync(string threadId, CancellationToken cancellationToken = default)
    {
        if (CurrentUser is null)
        {
            return false;
        }

        var partitionKey = _identityHasher.ForEmail(CurrentUser.Email).Value;
        var thread = await _threadStore.GetAsync(threadId, partitionKey, cancellationToken);
        if (thread is null)
        {
            // FR-013: a foreign or nonexistent thread both land here — never distinguishable.
            IsNotFound = true;
            ThreadId = null;
            ModelId = null;
            Messages.Clear();
            ErrorMessage = null;
            NotifyChanged();
            return false;
        }

        ThreadId = thread.Id;
        ModelId = thread.ModelId;
        IsNotFound = false;
        ErrorMessage = null;
        Messages.Clear();

        var history = await _messageStore.ListByThreadAsync(threadId, partitionKey, cancellationToken);
        Messages.AddRange(history.Select(message => new ChatMessageViewState
        {
            Role = message.Role == ChatMessageRole.User ? "user" : "assistant",
            Content = message.Content,
            IsComplete = true,
        }));

        NotifyChanged();
        return true;
    }

    /// <summary>Spec 024 US3 — returns to a fresh, blank conversation (Story 1's "/" contract). Synchronous — no I/O needed.</summary>
    public void ResetToNew()
    {
        ThreadId = null;
        ModelId = null;
        IsNotFound = false;
        ErrorMessage = null;
        ComposerText = string.Empty;
        Messages.Clear();
        NotifyChanged();
    }

    public async Task SendAsync(CancellationToken cancellationToken = default)
    {
        if (IsStreaming || string.IsNullOrWhiteSpace(ComposerText) || CurrentUser is null)
        {
            return;
        }

        var caller = CurrentUser;
        var messageText = ComposerText;
        ErrorMessage = null;

        if (ThreadId is null && !await TryCreateThreadAsync(caller, cancellationToken))
        {
            return; // ErrorMessage already set by TryCreateThreadAsync
        }

        ComposerText = string.Empty;
        Messages.Add(new ChatMessageViewState { Role = "user", Content = messageText, IsComplete = true });
        IsStreaming = true;
        NotifyChanged();

        var assistantMessage = new ChatMessageViewState { Role = "assistant", IsComplete = false };
        Messages.Add(assistantMessage);

        ChatSendResult result;
        try
        {
            result = await _chatPipeline.SendMessageAsync(caller, ThreadId!, messageText, ModelId!, cancellationToken);
        }
        catch (Exception)
        {
            // FR-004: unexpected failures show a generic message, never internal detail; the
            // typed text is restored so the user can retry.
            Messages.Remove(assistantMessage);
            ErrorMessage = "An unexpected error occurred. Please try again.";
            ComposerText = messageText;
            IsStreaming = false;
            return;
        }

        switch (result)
        {
            case ChatSendResult.Rejected rejected:
                Messages.Remove(assistantMessage);
                ErrorMessage = DescribeRejection(rejected);
                ComposerText = messageText;
                break;

            case ChatSendResult.ContentBlocked blocked:
                Messages.Remove(assistantMessage);
                ErrorMessage = $"Your message was blocked ({blocked.Category}). Please rephrase and try again.";
                ComposerText = messageText;
                break;

            case ChatSendResult.Streaming streaming:
                await ConsumeStreamAsync(streaming.Chunks, assistantMessage, cancellationToken);
                break;

            default:
                Messages.Remove(assistantMessage);
                ErrorMessage = "An unexpected error occurred. Please try again.";
                ComposerText = messageText;
                break;
        }

        IsStreaming = false;
    }

    private async Task<bool> TryCreateThreadAsync(UserModel caller, CancellationToken cancellationToken)
    {
        var modelsResult = await _modelAccessService.GetAvailableModelsAsync(caller, cancellationToken);
        if (!modelsResult.IsSuccess || modelsResult.Response is not { Count: > 0 } models)
        {
            ErrorMessage = "No models are currently available for your account.";
            return false;
        }

        // No "default model" concept exists yet (see research.md) — the first entitled model is
        // an MVP-scoped UI choice, not a new backend capability.
        ModelId = models[0].Id;

        var partitionKey = _identityHasher.ForEmail(caller.Email).Value;
        var thread = await _threadStore.CreateAsync(partitionKey, caller.Email, ModelId, cancellationToken: cancellationToken);
        ThreadId = thread.Id;
        return true;
    }

    private async Task ConsumeStreamAsync(
        IAsyncEnumerable<string> chunks, ChatMessageViewState assistantMessage, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var chunk in chunks.WithCancellation(cancellationToken))
            {
                assistantMessage.Content += chunk;
                NotifyChanged();
            }

            assistantMessage.IsComplete = true;
        }
        catch (Exception)
        {
            // FR-012: a partial response stays visible, visibly marked as interrupted — never
            // silently dropped or presented as a normal completion.
            assistantMessage.IsInterrupted = true;
            assistantMessage.IsComplete = true;
            ErrorMessage = "The connection was interrupted before the response finished.";
        }
    }

    private static string DescribeRejection(ChatSendResult.Rejected rejected) => rejected.Code switch
    {
        PreflightRejectionCode.ThreadReadOnly => "This conversation can no longer accept new messages.",
        PreflightRejectionCode.MessageTooLong => "Your message is too long. Please shorten it and try again.",
        PreflightRejectionCode.DailyLimitExceeded => rejected.ResetsAtUtc is { } resetsAt
            ? $"You've reached your daily message limit. It resets at {resetsAt:t}."
            : "You've reached your daily message limit.",
        _ => "An unexpected error occurred. Please try again.",
    };

    private void NotifyChanged() => OnChanged?.Invoke();
}
