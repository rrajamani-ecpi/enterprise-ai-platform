# Contract: Route Table

**Feature**: 016-prompt-crud-sharing-ownership-transfer

Minimal-API routes (`PromptEndpoints.MapPromptEndpoints`), matching `ChatEndpoints`/`ModelAccessEndpoints`/`PersonaEndpoints`'s `/api/{feature}/{resource}` convention. No prompt route is policy-declarable beyond authentication — ownership/collaborator/share-target gating is resource-specific and is checked in-handler via `PromptAccessEvaluator`.

Note the deliberate divergence from spec 009: persona transfer is route-gated `RequireAdmin`, but **prompt transfer is authorized for the owner *or* an admin** (FR-006). A route-level `RequireAdmin` would reject the owner, so the disjunction is evaluated in-handler by `PromptAccessEvaluator.CanTransfer`.

| Route | Purpose | Requirement |
|---|---|---|
| `GET /api/prompts` | List prompts visible to the caller (owner, admin, collaborator, or share target) | Authenticated (`RequireAuthenticated` — deny-by-default fallback policy); the visibility filter is applied server-side, so an unshared prompt never appears (FR-001/FR-002) |
| `GET /api/prompts/{id}` | Read a single prompt | Authenticated; `PromptAccessEvaluator.CanRead` in-handler (FR-001/FR-002/FR-009) |
| `POST /api/prompts` | Create a prompt | Authenticated; `PromptValidationRules.TryValidate` before persistence (FR-003/FR-013); `SharedWith` entries validated via `ISharingPolicyService` (FR-004); owner fields server-derived from `ICurrentUserAccessor` |
| `PATCH /api/prompts/{id}` | Update a prompt | Authenticated; `PromptAccessEvaluator.CanWrite` in-handler — admin, owner, or collaborator (FR-001/FR-014); `PromptValidationRules` re-validated (FR-003); `SharedWith` re-validated via `ISharingPolicyService` (FR-004); `RowVersion` conflict → 409 (FR-007) |
| `DELETE /api/prompts/{id}` | Delete a prompt | Authenticated; `PromptAccessEvaluator.CanWrite` in-handler; favorites removed by DB cascade, not by handler code (FR-015/FR-017) |
| `POST /api/prompts/{id}/transfer-ownership` | Transfer ownership to a new owner | Authenticated; `PromptAccessEvaluator.CanTransfer` in-handler — owner or admin (FR-006). Request body is `TransferPromptOwnershipRequest { NewOwnerEmail }` **and nothing else** — see the field-injection note below (FR-005). Single-row update; `Id` preserved (FR-008); `RowVersion` conflict → 409 (FR-007); favorites untouched (FR-019) |
| `GET /api/prompts/favorites` | List the caller's favorites | Authenticated; scoped to the caller's own `UserPartitionKey` server-side (FR-016/SC-007) |
| `POST /api/prompts/{id}/favorite` | Add a favorite | Authenticated; `PromptAccessEvaluator.CanRead` checked first — a prompt the caller cannot read cannot be favorited (FR-016); idempotent by composite PK |
| `DELETE /api/prompts/{id}/favorite` | Remove a favorite | Authenticated; scoped to the caller's own `UserPartitionKey`; idempotent |
| `POST /api/promptGenerator` | AI-assisted prompt generation | Authenticated; resolves `PrimaryModelId` → `FallbackModelId` from `PersonaGenerationModelConfig` (FR-010); total failure returns a **structured JSON error body**, never plain text and never a fabricated success (FR-011) |

## Enumeration-prevention note (FR-009)

Every in-handler `PromptAccessEvaluator` rejection on `GET /{id}`, `PATCH`, `DELETE`, `transfer-ownership`, and the favorite routes maps to the same fixed `ServerActionResponse.Unauthorized("You do not have access to this prompt.")` → `Results.Json(..., statusCode: 401)`, **regardless of whether the prompt exists**.

`ResponseStatus.NOT_FOUND` must never be returned from these routes: it maps to HTTP 404 while a denial maps to 401, so the status code itself would disclose existence — precisely the enumeration signal FR-009 forbids.

## Field-injection note (FR-005)

`TransferPromptOwnershipRequest` declares exactly one property, `NewOwnerEmail`. There is no `name`, `description`, `createdAt`, `sharedWith`, `collaboratorPartitionKeys`, or `ownerUserId` property for the model binder to populate, so forged values in a transfer payload are discarded before any handler code executes. Every non-ownership field on the persisted row is re-read from the database and written back unchanged. SC-001 therefore holds structurally rather than by validation.
