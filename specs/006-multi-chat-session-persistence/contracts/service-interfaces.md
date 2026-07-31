# Contract: Service Interfaces

**Feature**: 006-multi-chat-session-persistence

Internal C# interfaces. Signatures are the contract; bodies belong to implementation/tasks. Reuses spec 002's `ServerActionResponse<T>`/`UserModel`, spec 004's `IChatPipeline`/`IChatThreadStore`/`ChatRequest`, and spec 014's `IModelCatalogService` rather than redefining equivalents (Principle IV).

## `IMultiChatSessionStore` (FR-001–FR-005, D1/D2)

```csharp
public interface IMultiChatSessionStore
{
    // Get-or-create: a caller with no session yet gets a fresh 2-quadrant default (FR-002).
    Task<MultiChatSession> GetOrCreateAsync(string ownerPartitionKey, string ownerUserId, CancellationToken ct = default);

    Task<ServerActionResponse<MultiChatSession>> AddQuadrantAsync(
        string ownerPartitionKey, CancellationToken ct = default); // rejects at cap=4 (FR-005)

    // At floor=2, clears the highest-position quadrant's assignment instead of removing it (FR-004).
    Task<MultiChatSession> RemoveQuadrantAsync(string ownerPartitionKey, CancellationToken ct = default);

    Task<MultiChatSession> AssignModelAsync(
        string ownerPartitionKey, int position, string modelId, CancellationToken ct = default);

    // Sets ThreadId on a specific quadrant — called once per quadrant's first send (FR-003).
    Task<MultiChatSession> SetQuadrantThreadAsync(
        string ownerPartitionKey, int position, string threadId, CancellationToken ct = default);
}
```

- **Contract**: `Quadrants.Count` is always in `[2, 4]` after any call — enforced here, not by the caller. Exactly one implementation (architecture-tested).

## `IChatThreadStore.CreateAsync` (spec 004, extended — D5)

```csharp
public interface IChatThreadStore
{
    Task<ChatThreadModel?> GetAsync(string threadId, string ownerPartitionKey, CancellationToken cancellationToken = default);

    Task<ChatThreadModel> CreateAsync(
        string ownerPartitionKey,
        string ownerUserId,
        string modelId,
        string? multiChatSessionId = null,   // NEW — spec 006
        int? multiChatPosition = null,       // NEW — spec 006
        CancellationToken cancellationToken = default);
}
```

- **Contract**: Existing spec 004 call sites (single-thread send) omit the new parameters and get `null`/`null` — unaffected behavior (backward-compatible extension, not a breaking change).

## Parallel-send orchestration (D4 — no new interface; a concrete dispatcher over existing contracts)

```csharp
// Not a new abstraction — MultiChatDispatcher (concrete, Infrastructure) directly composes:
//   IMultiChatSessionStore (read quadrants) + IChatThreadStore.CreateAsync (D3) +
//   IChatPipeline.SendMessageAsync (one call per quadrant, run concurrently) +
//   a Channel<QuadrantEvent> fan-in merge (D4).
// No IMultiChatDispatcher interface is introduced in R1 — there is exactly one orchestration
// shape needed (fan out to N quadrants, merge to one tagged stream) and no second implementation
// or substitution point is required yet; the endpoint depends on the concrete type directly.
```

## Reused from specs 002/004/014 (not redefined here)

- `ServerActionResponse<T>` / `UserModel` / `StoragePartitionKey` / `IIdentityHasher` (spec 002).
- `IChatPipeline` / `ChatSendResult` / `ChatRequest` / `ChatMessage` (spec 004).
- `IModelCatalogService` (spec 014) — validates a quadrant's assigned model before `AssignModelAsync` persists it.
