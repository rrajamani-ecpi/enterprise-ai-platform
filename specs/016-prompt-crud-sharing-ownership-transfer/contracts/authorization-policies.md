# Contract: Authorization Policies

**Feature**: 016-prompt-crud-sharing-ownership-transfer

How prompt authorization composes, and what a caller is allowed to learn from a denial.

## Two layers, different jobs

| Layer | Mechanism | What it decides |
|---|---|---|
| Route-declarable | ASP.NET Core authorization policy (`PolicyNames`) | *Whether the caller may reach the endpoint at all.* For prompts this is only `RequireAuthenticated` — the deny-by-default fallback policy from spec 002. |
| In-handler | `PromptAccessEvaluator` (static, pure) | *Whether this specific caller may act on this specific prompt.* Resource-specific, so it cannot be expressed as a route policy. |

Unlike spec 009, **no prompt route carries `RequireAdmin`** — not even transfer. FR-006 authorizes transfer for the **owner or an admin**, and a route-level `RequireAdmin` would reject the owner, who is the primary intended caller. The disjunction is therefore evaluated in-handler by `PromptAccessEvaluator.CanTransfer`. Admin rights are read from the server-derived `UserModel.RoleFlags`, never from request data.

## The access matrix

| Caller relationship | Read | Write | Transfer |
|---|---|---|---|
| Owner (`OwnerPartitionKey` match) | ✅ | ✅ | ✅ |
| Admin (`caller.IsAdmin`) | ✅ | ✅ | ✅ |
| Collaborator (`CollaboratorPartitionKeys`) | ✅ | ✅ | ❌ |
| Individual share target (`SharedWith`) | ✅ | ❌ | ❌ |
| Group share target (token ∈ `UserModel.GroupTokens`) | ✅ | ❌ | ❌ |
| Unrelated authenticated user | ❌ | ❌ | ❌ |

Sharing is strictly **read-only** — no share grant of either kind ever confers write or transfer (FR-002). Transfer is narrower than write in exactly one place: a **collaborator** may edit a prompt but may not give it away (FR-006 names only owner and admin), so `CanTransfer` is not simply an alias for `CanWrite`.

Every comparison is against hashed identities produced by `IIdentityHasher`; raw emails are stored for display and audit only and are never used in a decision.

## Group tokens

`Group` share targets are matched against `UserModel.GroupTokens`, populated by `RoleClaimsTransformation` from the **Entra `groups` claim** — a server-verified claim from the validated token. Group membership is never read from request data, query strings, or client state.

This field does not exist yet; adding it is the one cross-spec change this feature requires. See [research.md D6](../research.md) for the rationale and the individual-only fallback.

## Non-revealing error contract (FR-009)

Every denial from `GET /api/prompts/{id}`, `PATCH`, `DELETE`, `POST /{id}/transfer-ownership`, and the favorite routes returns:

```
HTTP/1.1 401 Unauthorized
Content-Type: application/json

{ "status": "UNAUTHORIZED", "message": "You do not have access to this prompt." }
```

**This is byte-identical whether the prompt exists, was deleted, or never existed.** The response must not vary in status code, message, headers, or timing-visible branch structure.

Concretely, this forbids the natural-looking implementation:

```csharp
// WRONG — the status code itself is the enumeration oracle
var prompt = await db.Prompts.FindAsync(id);
if (prompt is null) return ServerActionResponse<T>.NotFound();          // → 404 "doesn't exist"
if (!PromptAccessEvaluator.CanRead(prompt, caller))
    return ServerActionResponse<T>.Unauthorized(UnauthorizedMessage);   // → 401 "exists, not yours"
```

`ResponseStatus.NOT_FOUND` maps to HTTP 404 and `UNAUTHORIZED` maps to 401, so the split above lets an unauthorized caller enumerate valid prompt ids by status code alone. The required shape collapses both branches:

```csharp
var prompt = await db.Prompts.FindAsync(id);
if (prompt is null || !PromptAccessEvaluator.CanRead(prompt, caller))
    return ServerActionResponse<T>.Unauthorized(UnauthorizedMessage);
```

`NOT_FOUND` remains legitimate on ungated routes; it is banned only from these resource-gated paths. SC-005 asserts this directly by comparing the full responses for an existing-but-forbidden prompt and a nonexistent id.

## Field-injection defence (FR-005 / SC-001)

The transfer request type carries **only** `NewOwnerEmail`. Because no property exists for `name`, `description`, `createdAt`, `sharedWith`, `collaboratorPartitionKeys`, or `ownerUserId`, forged values are discarded by the model binder before handler code runs — there is no validation branch that could be forgotten or bypassed.

The handler additionally re-reads the persisted row and writes back every non-ownership field unchanged, so even a future DTO change cannot silently turn transfer into a general-purpose update.

## Concurrency (FR-007)

Concurrent transfers of the same prompt are resolved by the `RowVersion` concurrency token: the first write wins, the second returns HTTP 409 with `ConcurrencyConflictMessage`. There is no last-write-wins path and no silent retry (Principle III). Because transfer is a single-row `UPDATE` with an immutable `Id`, a conflict leaves the original row fully intact — the "zero or two copies" outcome the spec describes for the legacy Cosmos implementation is unreachable here (FR-008).
