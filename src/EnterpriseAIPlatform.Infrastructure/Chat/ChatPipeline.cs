using System.Runtime.CompilerServices;
using System.Text;
using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.Chat;
using EnterpriseAIPlatform.Domain.ModelAccess;

namespace EnterpriseAIPlatform.Infrastructure.Chat;

/// <summary>
/// The single implementation of <see cref="IChatPipeline"/> (spec 004). Fixed gate ordering per
/// the interface contract: thread-version gate → message-limit preflight (fail-open only on a
/// read failure, FR-005) → dataProducts override (no-op in R1) → Content Safety → PII redaction
/// (model-bound copy only) → model-access resolution (spec 014) → model invocation/streaming →
/// persistence. No store write happens before every gate passes (FR-001).
/// </summary>
public sealed class ChatPipeline : IChatPipeline
{
    private readonly IChatThreadStore _threadStore;
    private readonly IChatMessageStore _messageStore;
    private readonly IIdentityHasher _identityHasher;
    private readonly IMessageLimitConfigService _messageLimitConfigService;
    private readonly IDailyMessageCounter _dailyMessageCounter;
    private readonly IContentSafetyGuard _contentSafetyGuard;
    private readonly IPiiRedactor _piiRedactor;
    private readonly IModelAccessService _modelAccessService;
    private readonly IModelCatalogService _modelCatalogService;
    private readonly ISystemModelConfigCache _systemModelConfigCache;
    private readonly IEnumerable<IChatCompletionClient> _chatCompletionClients;

    public ChatPipeline(
        IChatThreadStore threadStore,
        IChatMessageStore messageStore,
        IIdentityHasher identityHasher,
        IMessageLimitConfigService messageLimitConfigService,
        IDailyMessageCounter dailyMessageCounter,
        IContentSafetyGuard contentSafetyGuard,
        IPiiRedactor piiRedactor,
        IModelAccessService modelAccessService,
        IModelCatalogService modelCatalogService,
        ISystemModelConfigCache systemModelConfigCache,
        IEnumerable<IChatCompletionClient> chatCompletionClients)
    {
        _threadStore = threadStore;
        _messageStore = messageStore;
        _identityHasher = identityHasher;
        _messageLimitConfigService = messageLimitConfigService;
        _dailyMessageCounter = dailyMessageCounter;
        _contentSafetyGuard = contentSafetyGuard;
        _piiRedactor = piiRedactor;
        _modelAccessService = modelAccessService;
        _modelCatalogService = modelCatalogService;
        _systemModelConfigCache = systemModelConfigCache;
        _chatCompletionClients = chatCompletionClients;
    }

    public async Task<ChatSendResult> SendMessageAsync(
        UserModel caller, string threadId, string userText, string requestedModelId, CancellationToken cancellationToken = default)
    {
        var partitionKey = _identityHasher.ForEmail(caller.Email).Value;

        // 1. Thread-version gate (FR-002) — a read, not a write; no record is touched.
        var thread = await _threadStore.GetAsync(threadId, partitionKey, cancellationToken);
        if (thread is null || thread.Version != "v3")
        {
            return new ChatSendResult.Rejected(PreflightRejectionCode.ThreadReadOnly);
        }

        // 2. Message-limit preflight (FR-003/004/005) — still before any store write.
        var preflight = await RunMessageLimitPreflightAsync(partitionKey, userText, cancellationToken);
        if (!preflight.IsAllowed)
        {
            return new ChatSendResult.Rejected(preflight.RejectionCode!.Value, preflight.ResetsAtUtc);
        }

        // 3. dataProducts server-authoritative override (FR-006) — thread.DataProducts is the only
        //    source ever used; no client-supplied value is accepted by this method's signature at all.
        _ = thread.DataProducts;

        // 4. Content Safety check on the ORIGINAL user text, before redaction (D6/D7 ordering).
        var safetyVerdict = await _contentSafetyGuard.CheckAsync(userText, cancellationToken);
        if (!safetyVerdict.IsAllowed)
        {
            return new ChatSendResult.ContentBlocked(safetyVerdict.Category ?? "unknown");
        }

        // 5. PII redaction — the model-bound copy only; `userText` (persisted below) stays original (FR-008).
        var redaction = _piiRedactor.Redact(userText);

        // 6. Model-access resolution (FR-020/021, spec 014) — never trusts the requested model id verbatim.
        var resolvedModelId = await ResolveEffectiveModelIdAsync(caller, requestedModelId, cancellationToken);
        var modelResult = await _modelCatalogService.GetAsync(resolvedModelId, cancellationToken: cancellationToken);
        var model = modelResult.Response
            ?? throw new InvalidOperationException($"Resolved model '{resolvedModelId}' was not found in the catalog.");

        // 7. Model invocation/streaming.
        var client = _chatCompletionClients.FirstOrDefault(c => c.Provider == model.Provider)
            ?? throw new InvalidOperationException($"No IChatCompletionClient is registered for provider '{model.Provider}'.");
        var chatRequest = new ChatRequest(new[] { new ChatMessage("user", redaction.RedactedText) });
        var chunks = client.StreamCompletionAsync(chatRequest, model, cancellationToken);

        // 8. Persistence happens only after the gates above passed, streamed inline with the response.
        var persistedStream = PersistAfterStreamingAsync(partitionKey, threadId, userText, chunks, cancellationToken);
        return new ChatSendResult.Streaming(persistedStream);
    }

    private async IAsyncEnumerable<string> PersistAfterStreamingAsync(
        string partitionKey,
        string threadId,
        string userText,
        IAsyncEnumerable<string> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await _messageStore.AppendAsync(
            new ChatMessageModel
            {
                Id = Guid.NewGuid().ToString("n"),
                PartitionKey = partitionKey,
                ThreadId = threadId,
                Role = ChatMessageRole.User,
                Content = userText,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            },
            cancellationToken);

        var assembled = new StringBuilder();
        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            assembled.Append(chunk);
            yield return chunk;
        }

        await _messageStore.AppendAsync(
            new ChatMessageModel
            {
                Id = Guid.NewGuid().ToString("n"),
                PartitionKey = partitionKey,
                ThreadId = threadId,
                Role = ChatMessageRole.Assistant,
                Content = assembled.ToString(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            },
            cancellationToken);
    }

    private async Task<PreflightResult> RunMessageLimitPreflightAsync(
        string partitionKey, string userText, CancellationToken cancellationToken)
    {
        MessageLimitConfig config;
        try
        {
            var configResult = await _messageLimitConfigService.GetAsync(cancellationToken);
            if (configResult.Status != ResponseStatus.OK)
            {
                return PreflightResult.Allowed(); // fail open — config unreadable (FR-005)
            }

            config = configResult.Response!;
        }
        catch
        {
            return PreflightResult.Allowed(); // fail open — config store unreachable (FR-005)
        }

        if (config.PerMessageCharacterCap is int cap && userText.Length > cap)
        {
            return PreflightResult.Rejected(PreflightRejectionCode.MessageTooLong);
        }

        if (config.DailyMessageCap is int dailyCap)
        {
            // DailyMessageCounter itself fails open (returns null) on a read failure.
            var count = await _dailyMessageCounter.GetCountAsync(partitionKey, cancellationToken);
            if (count is not null && count >= dailyCap)
            {
                return PreflightResult.Rejected(PreflightRejectionCode.DailyLimitExceeded, _dailyMessageCounter.NextResetUtc());
            }
        }

        await _dailyMessageCounter.IncrementAsync(partitionKey, cancellationToken);
        return PreflightResult.Allowed();
    }

    private async Task<string> ResolveEffectiveModelIdAsync(
        UserModel caller, string requestedModelId, CancellationToken cancellationToken)
    {
        var available = await _modelAccessService.GetAvailableModelsAsync(caller, cancellationToken);
        if (available.Response is not null && available.Response.Any(m => m.Id == requestedModelId))
        {
            return requestedModelId;
        }

        var systemConfig = await _systemModelConfigCache.GetAsync(cancellationToken);
        return systemConfig.FallbackModelId;
    }
}
