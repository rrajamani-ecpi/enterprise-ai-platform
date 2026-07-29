using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Domain.Chat;

namespace EnterpriseAIPlatform.Application.Chat;

/// <summary>
/// The single orchestration point for send → stream → persist (spec 004 FR-001–FR-006, FR-020/021,
/// FR-024). Fixed ordering: thread-version gate → message-limit preflight (fail-open only on the
/// documented read-failure case) → dataProducts override → Content Safety check on the original
/// user text → PII redaction (model-bound copy only) → model-access resolution (spec 014) →
/// model invocation/streaming → persistence. No store write happens before every gate passes.
/// </summary>
public interface IChatPipeline
{
    Task<ChatSendResult> SendMessageAsync(
        UserModel caller,
        string threadId,
        string userText,
        string requestedModelId,
        CancellationToken cancellationToken = default);
}

public abstract record ChatSendResult
{
    private ChatSendResult()
    {
    }

    public sealed record Rejected(PreflightRejectionCode Code, DateTimeOffset? ResetsAtUtc = null) : ChatSendResult;

    public sealed record ContentBlocked(string Category) : ChatSendResult;

    public sealed record Streaming(IAsyncEnumerable<string> Chunks) : ChatSendResult;
}
