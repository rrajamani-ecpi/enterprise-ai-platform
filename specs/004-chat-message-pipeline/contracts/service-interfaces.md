# Contract: Service Interfaces

**Feature**: 004-chat-message-pipeline

Internal C# interfaces. Signatures are the contract; bodies belong to implementation/tasks. Reuses spec 002's `ServerActionResponse<T>`/`UserModel` and spec 014's `IModelAccessService`/`IModelProviderAdapter` rather than redefining equivalents (Principle IV).

## `IChatPipeline` (FR-001–FR-006, FR-020/021, FR-024, US1/US5)

```csharp
public interface IChatPipeline
{
    // Runs the full gate (thread-version, message-limit, dataProducts override), resolves the
    // effective model via spec 014, invokes the model, and streams the response. Persists the
    // user + assistant ChatMessageModel only after all gates pass and the stream completes.
    // Throws nothing to the caller for expected rejections — returns a discriminated result the
    // endpoint maps to the correct status code (see route-table.md); unhandled exceptions are
    // caught at the endpoint's top-level boundary and surfaced as a generic 500 (FR-024).
    Task<ChatSendResult> SendMessageAsync(
        UserModel caller, string threadId, string userText, string requestedModelId,
        CancellationToken cancellationToken = default);
}

public abstract record ChatSendResult
{
    public sealed record Rejected(PreflightRejectionCode Code, DateTimeOffset? ResetsAtUtc) : ChatSendResult;
    public sealed record ContentBlocked(string Category) : ChatSendResult;
    public sealed record Streaming(IAsyncEnumerable<string> Chunks) : ChatSendResult;
}

public enum PreflightRejectionCode { ThreadReadOnly, MessageTooLong, DailyLimitExceeded }
```

- **Contract**: Exactly one implementation (architecture-tested). Ordering is fixed: version gate → message-limit preflight (fail-open only on FR-005's read-failure case) → `dataProducts` override → Content Safety check on the user text → PII redaction (model-bound copy only) → model-access resolution (spec 014) → model invocation/streaming → persistence.

## `IChatThreadStore` / `IChatMessageStore` (Cosmos-backed, D1)

```csharp
public interface IChatThreadStore
{
    Task<ChatThreadModel?> GetAsync(string threadId, string ownerPartitionKey, CancellationToken ct = default);
    Task<ChatThreadModel> CreateAsync(string ownerPartitionKey, string ownerUserId, string modelId, CancellationToken ct = default);
}

public interface IChatMessageStore
{
    Task AppendAsync(ChatMessageModel message, CancellationToken ct = default);
}
```

- **Contract**: `CreateAsync` always creates a `Version = "v3"` thread (FR-002). Neither store is called by `IChatPipeline` until all preflight gates pass (FR-001).

## `IPiiRedactor` (FR-008, D6 — R1: one implementation)

```csharp
public interface IPiiRedactor
{
    PiiRedactionResult Redact(string userText);
}
```

- **Contract**: Pure/synchronous — no external call in R1 (`RegexPiiRedactor`). Applies only to the model-bound copy of user-authored text; the persisted `ChatMessageModel.Content` is never the redacted copy (FR-008).

## `IContentSafetyGuard` (constitution addition, D7 — R1: one implementation)

```csharp
public interface IContentSafetyGuard
{
    Task<ContentSafetyVerdict> CheckAsync(string text, CancellationToken cancellationToken = default);
}
```

- **Contract**: `AzureContentSafetyGuard` calls Azure AI Content Safety via workload-identity-authenticated `HttpClient`. Unconfigured in Production fails app startup; unconfigured in Development logs and allows through (D7). Runs on the user text before the model call — this is a gate, not a persistence concern.

## `IChatCompletionClient` (FR-009 pattern extended, D4/D8/D9 — R1: one implementation)

```csharp
public interface IChatCompletionClient
{
    string Provider { get; }
    IAsyncEnumerable<string> StreamCompletionAsync(
        ChatRequest request, ModelConfigDocument model, CancellationToken cancellationToken = default);
}
```

- **Contract**: `AzureFoundryChatCompletionClient` uses spec 014's `IModelProviderAdapter` (keyed by `model.Provider`) for request/response shaping and the workload-identity auth header, wraps the actual HTTP call in a `Microsoft.Extensions.Http.Resilience` standard resilience handler (timeout + retry + circuit breaker), and yields decoded content chunks. Reuses spec 014's `ChatRequest`/`ChatMessage` types verbatim (Principle IV) rather than redefining a parallel shape.

## Reused from specs 002/014 (not redefined here)

- `ServerActionResponse<T>` / `UserModel` / `StoragePartitionKey` / `IIdentityHasher` (spec 002).
- `IModelAccessService` / `IModelCatalogService` / `SystemModelConfig` / `IModelProviderAdapter` / `ChatRequest` / `ChatMessage` (spec 014).
