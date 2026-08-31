# Contract: Service Interfaces

**Feature**: 016-prompt-crud-sharing-ownership-transfer

Internal (in-process) contracts. All live in `EnterpriseAIPlatform.Application/Prompts/`; implementations live in `EnterpriseAIPlatform.Infrastructure/Prompts/`, matching the layering used by specs 014/017/018/009.

---

## `IPromptService`

The single Application-layer seam for prompt persistence. Every method takes the caller's `UserModel` — no method infers identity from ambient state, and no method accepts a caller identity from request data (Principle II).

| Member | Contract |
|---|---|
| `ListAsync(UserModel caller, CancellationToken)` | Returns only prompts the caller may read (FR-001/FR-002). Filtering is server-side; an unauthorized prompt is never returned and then hidden by the UI. |
| `GetAsync(string id, UserModel caller, CancellationToken)` | `PromptAccessEvaluator.CanRead` first. On denial **or** absence returns the identical `Unauthorized` response (FR-009). |
| `CreateAsync(PromptModel prompt, UserModel caller, CancellationToken)` | Validates via `PromptValidationRules` (FR-003), validates share targets via `ISharingPolicyService` (FR-004), server-derives `OwnerUserId`/`OwnerPartitionKey`/`CreatedAtUtc`/`RowVersion`. |
| `UpdateAsync(PromptModel prompt, UserModel caller, CancellationToken)` | `CanWrite` first; re-validates (FR-003/FR-004); `RowVersion` mismatch returns the shared `ConcurrencyConflictMessage` → 409. |
| `DeleteAsync(string id, UserModel caller, CancellationToken)` | `CanWrite` first. Deletes the row; favorites disappear by DB cascade (FR-015/FR-017). |
| `TransferOwnershipAsync(string id, string newOwnerEmail, UserModel caller, CancellationToken)` | `CanTransfer` first (FR-006). Loads the row, mutates **only** `OwnerUserId`/`OwnerPartitionKey`/`UpdatedAtUtc`/`RowVersion`, saves once (FR-005/FR-007/FR-008). Favorites untouched (FR-019). |
| `ListFavoritesAsync(UserModel caller, CancellationToken)` | Scoped to the caller's own `UserPartitionKey` only (SC-007). |
| `AddFavoriteAsync(string promptId, UserModel caller, CancellationToken)` | `CanRead` first (FR-016). Idempotent — a repeat call is a success, not a duplicate-key error. |
| `RemoveFavoriteAsync(string promptId, UserModel caller, CancellationToken)` | Scoped to the caller. Idempotent. |

All methods return `ServerActionResponse<T>` (spec 002), so failures are explicit values rather than exceptions or nulls (Principle III).

**Error contract**: exactly two failure messages are exposed from gated paths — the fixed unauthorized string, and `ConcurrencyConflictMessage`. Neither varies by whether the resource exists.

---

## `IPromptGenerationService`

| Member | Contract |
|---|---|
| `GenerateAsync(PromptGenerationRequest request, UserModel caller, CancellationToken)` | Resolves `PrimaryModelId`, then `FallbackModelId` on primary failure — **at most one fallback attempt** (FR-010). Both must be members of `AllowedModelIds`; an out-of-set id is a configuration error, not a silent pass-through. |

**Streaming note**: `IChatCompletionClient` exposes only `IAsyncEnumerable<string> StreamCompletionAsync(...)`. Generation **accumulates chunks into a single string** rather than adding a second non-streaming method to that interface (Principle IV).

**Failure contract (FR-011)**: when the primary fails and either no fallback is configured or the fallback also fails, the response is a structured JSON error object with an `application/json` content type. It must never be a plain-text body, and must never be a success-shaped response containing an error message as its generated content (Principle III).

---

## `PromptAccessEvaluator` (static, DI-free)

Pure function over `(PromptModel, UserModel, string callerPartitionKey)` — no `DbContext`, no `HttpContext`, no service dependencies, matching `PersonaAccessEvaluator`'s exact signature shape. The hashed caller identity is passed in rather than computed, so the evaluator stays DI-free (no `IIdentityHasher` dependency).

| Member | Rule |
|---|---|
| `CanRead(PromptModel, UserModel, string callerPartitionKey)` | `true` if the caller is the owner (`OwnerPartitionKey` match), an admin (`caller.IsAdmin`), a collaborator (`CollaboratorPartitionKeys` contains `callerPartitionKey`), an `Individual` share target matching `callerPartitionKey`, or a `Group` share target whose token is in `caller.GroupTokens` (FR-001/FR-002). |
| `CanWrite(PromptModel, UserModel, string callerPartitionKey)` | Admin, owner, or collaborator **only** (FR-001). Share targets are read-only — a share grant never confers write (FR-002). |
| `CanTransfer(PromptModel, UserModel, string callerPartitionKey)` | Owner **or admin** only (FR-006). Deliberately narrower than `CanWrite`: a collaborator may edit but may not give the prompt away. |

Comparisons use hashed partition keys via `IIdentityHasher`, never raw emails. This is the **only** place prompt access is decided; endpoints and UI both call it (Principle IV, architecture-tested).

**Unit-testable matrix (SC-004)**: role × {owner, admin, collaborator, individual-share, group-share, unrelated} × {read, write, transfer}, exercised without any host.

---

## `PromptValidationRules` (static, DI-free)

| Member | Rule |
|---|---|
| `TryValidate(PromptModel, out string error)` | `Name` and `Description` must be non-empty and non-whitespace (FR-003/FR-013/FR-014). |

Called from the Application layer on every create and update, so a direct API caller is bound by the same rule as a UI user (Principle V). Consistent with the codebase's no-DataAnnotations / no-FluentValidation convention.

---

## `ISharingPolicyService` (spec 018 — consumed, not defined here)

This feature is 018's **first consumer**. Every `SharedWith` entry on create/update is passed as a `ShareTargetRequest(ShareTargetType, GroupToken?)` and accepted only when the returned `SharingDecision.IsAllowed` is true; the `SharingDecisionReason` is surfaced in the validation error (FR-004).

No prompt-local sharing rule is defined anywhere in this feature — an architecture test asserts `PromptService` depends on `ISharingPolicyService` and that no second sharing evaluator exists (Principle IV).

---

## `ChatComposerState.SeedFromPrompt` (spec 024 — extended)

| Member | Contract |
|---|---|
| `SeedFromPrompt(PromptModel prompt)` | Assigns `prompt.Description` to `ComposerText` **verbatim** — no template substitution, truncation, or placeholder expansion (FR-012/FR-018) — and raises `OnChanged` so the composer re-renders. |

The user then edits and sends through the **existing** chat send path; this feature adds no second send route.
