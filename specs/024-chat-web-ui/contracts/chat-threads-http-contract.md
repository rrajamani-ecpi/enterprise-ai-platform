# HTTP Contract: Conversation List / History / Rename (Story 3)

These three routes extend spec 004's existing route surface in `ChatEndpoints.cs`, following the same convention already used there and in `MultiChatEndpoints.cs` (resolve caller via `ICurrentUserAccessor` → 401 if not OK → hash email via `IIdentityHasher.ForEmail` → call the store → map the result). They exist for consistency with the rest of the app's endpoint-exposed capabilities and for `WebApplicationFactory` integration testability — the Blazor UI itself calls `IChatThreadStore`/`IChatMessageStore` directly (see `ui-integration-contract.md`), not these routes.

## `GET /api/chat/threads`

Lists the caller's own conversations, most-recently-active first.

- **Auth**: any authenticated user (deny-by-default fallback policy).
- **Response 200**: `ConversationSummaryResponse[]`
  ```csharp
  public sealed record ConversationSummaryResponse(string Id, string DisplayName, DateTimeOffset LastActivityAtUtc, string ModelId);
  ```
- **Response 401**: caller not authenticated.
- Never includes another user's conversations (partition-key-scoped query — FR-005).

## `GET /api/chat/threads/{id}/messages`

Lists a conversation's full message history, oldest first.

- **Auth**: any authenticated user; the thread must belong to the caller.
- **Response 200**: `MessageResponse[]`
  ```csharp
  public sealed record MessageResponse(string Role, string Content, DateTimeOffset CreatedAtUtc);
  ```
- **Response 404**: the thread doesn't exist, or exists but isn't owned by the caller — both produce the identical response (FR-013; never reveals whether a foreign ID exists).
- **Response 401**: caller not authenticated.

## `PATCH /api/chat/threads/{id}`

Renames a conversation.

- **Auth**: any authenticated user; the thread must belong to the caller.
- **Request body**: `RenameThreadRequest(string DisplayName)`
- **Response 200**: `ConversationSummaryResponse` (updated) — name persisted.
- **Response 400** `{ "error": "EMPTY_NAME" }`: `DisplayName` was empty or whitespace-only after trimming — the prior name is left in effect (FR-007, Edge Cases).
- **Response 404**: thread doesn't exist or isn't owned by the caller (same non-disclosure guarantee as above).
- **Response 401**: caller not authenticated.

## Not covered by this contract

- Creating a thread and sending/streaming a message — see spec 004's existing `contracts/route-table.md` and `contracts/service-interfaces.md` (unchanged by Story 3).
- Deleting a conversation — not in scope for spec 024 (no story requests it).
