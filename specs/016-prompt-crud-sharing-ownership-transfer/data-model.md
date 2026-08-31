# Data Model: Prompt CRUD, Sharing & Ownership Transfer

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-08-31 | **Phase**: 1

Storage is **Azure SQL via EF Core** (`PromptDbContext`), per [research.md D1](./research.md). Two new entities plus three additive modifications to types owned by earlier specs.

---

## `PromptModel`

`EnterpriseAIPlatform.Domain.Prompts` — the reusable prompt template (spec Key Entities).

| Field | Type | Notes |
|---|---|---|
| `Id` | `string` (required) | PK. `Guid.NewGuid().ToString("N")`. **Never changes across a transfer** (FR-008). |
| `OwnerUserId` | `string` (required) | Current owner's raw email — display/audit only, never used for an access decision. |
| `OwnerPartitionKey` | `string` (required) | `IIdentityHasher.ForEmail(OwnerUserId)`. What `PromptAccessEvaluator` compares against. |
| `Name` | `string` (required) | Title. Non-empty (FR-003/FR-013). |
| `Description` | `string` (required) | **The reusable prompt text itself** — there is no separate `content`/`template` field. Non-empty (FR-003). Copied verbatim into the composer by FR-012. |
| `CollaboratorPartitionKeys` | `List<string>` | Hashed collaborator identities — full write access (FR-001). JSON column. |
| `SharedWith` | `List<PromptShareTarget>` | Read-only share grants (FR-002). JSON column. Validated against spec 018 on every write (FR-004). |
| `IsPublished` | `bool` | Legacy flag, superseded by `SharedWith`. Retained for migration fidelity; **not** consulted by any access decision. |
| `RowVersion` | `Guid` | Self-managed optimistic-concurrency token, `IsConcurrencyToken()` — see [research.md D10](./research.md). Regenerated on every successful write. |
| `CreatedAtUtc` | `DateTimeOffset` | Set once on create. **Server-derived; never accepted from a transfer request** (FR-005). |
| `UpdatedAtUtc` | `DateTimeOffset` | Touched on every write, including transfer. |

**Relationships**: one-to-many with `PromptFavorite` (cascade delete, FR-017).

**Validation** (`PromptValidationRules`, Application layer — Principle V):
- `Name` non-empty/non-whitespace, `Description` non-empty/non-whitespace (FR-003).
- Called from **every** create/update path, not only UI call sites.

**State transitions**: no lifecycle states. Ownership transfer mutates `OwnerUserId`/`OwnerPartitionKey`/`UpdatedAtUtc`/`RowVersion` **only** — every other field is re-read from the stored row and left untouched (FR-005), and there is no delete-then-recreate step (FR-007/FR-008).

---

## `PromptFavorite`

`EnterpriseAIPlatform.Domain.Prompts` — a per-user favorite. One row per (user, prompt); **not** a per-user array, see [research.md D7](./research.md).

| Field | Type | Notes |
|---|---|---|
| `UserPartitionKey` | `string` (required) | Composite PK part 1. Hashed caller identity. |
| `PromptId` | `string` (required) | Composite PK part 2. FK → `PromptModel.Id`, `OnDelete(DeleteBehavior.Cascade)`. |
| `FavoritedAtUtc` | `DateTimeOffset` | For stable ordering in the library view. |

**Why a composite key**: makes double-favoriting idempotent by constraint, and makes one user's favorites structurally incapable of appearing in another's list (SC-007).

**Why a cascade**: SC-006's "0 dangling references" becomes a property of the database rather than of every future delete path (Principle V, FR-017).

**Access rule**: a favorite may only be created for a prompt the caller can already read — `PromptAccessEvaluator.CanRead(...)` is checked before insert (FR-016). Transfer never touches favorites (FR-019).

---

## `PromptShareTarget`

`EnterpriseAIPlatform.Domain.Prompts` — a single read-grant, mirroring `PersonaShareTarget`'s shape.

| Field | Type | Notes |
|---|---|---|
| `Type` | `ShareTargetType` | Reuses spec 018's existing `Individual`/`Group` enum — not a new parallel enum (Principle IV). |
| `Identity` | `string?` | Set when `Type == Individual`. **Hashed** identity, never a raw email. |
| `GroupToken` | `string?` | Set when `Type == Group`. Matched against the caller's `UserModel.GroupTokens`. |

Factory helpers `ForIndividual(hashedIdentity)` / `ForGroup(token)` keep the two shapes mutually exclusive by construction.

---

## Modifications to existing entities

These three changes touch types owned by earlier, already-merged specs. All are additive with safe defaults.

### `UserModel` (spec 002) — `+ GroupTokens`

| Field | Type | Notes |
|---|---|---|
| `GroupTokens` | `IReadOnlyList<string>` | Defaults to empty. Populated by `RoleClaimsTransformation` from the Entra `groups` claim it **already reads** but currently discards after deriving role flags. |

Required by FR-002's group-token read half, which is otherwise unimplementable — see [research.md D6](./research.md) for the full rationale, the rejected alternatives, and the documented fallback. Empty default keeps every existing construction site (including tests) compiling unchanged.

### `PersonaGenerationModelConfig` (spec 014) — `+ PrimaryModelId`, `+ FallbackModelId`

| Field | Type | Notes |
|---|---|---|
| `PrimaryModelId` | `string?` | The model FR-010 calls first. MUST be a member of `AllowedModelIds`. |
| `FallbackModelId` | `string?` | Tried exactly once if the primary fails. MUST be a member of `AllowedModelIds`. Null ⇒ a primary failure goes straight to the FR-011 error response. |

The entity already scopes itself to "AI-assisted persona/**prompt** generation", so this extends the anticipated consumer rather than adding a parallel config (Principle IV; [research.md D8](./research.md)). Requires one additive EF Core migration on `ModelAccessDbContext`. Both remain read-open / admin-gated for writes, per spec 014 FR-005/FR-007.

### `ChatComposerState` (spec 024) — `+ SeedFromPrompt(...)`

Not persisted — a per-circuit Scoped view-state service. Adds a method that assigns `ComposerText` verbatim and raises `OnChanged`, satisfying FR-012/FR-018 by reusing the existing send path ([research.md D11](./research.md)).

---

## EF Core mapping notes (`PromptDbContext`)

Follows `PersonaDbContext`'s established pattern exactly:

- `List<string>` and `List<PromptShareTarget>` columns use a JSON `ValueConverter` **paired with a `ValueComparer`** — without the comparer, EF Core cannot detect in-place mutations of the collection and silently drops updates.
- `RowVersion` uses `IsConcurrencyToken()`, **not** `IsRowVersion()` — the latter binds to SQL Server's native auto-generated column type, which the InMemory provider used in tests does not emulate identically ([research.md D10](./research.md)).
- `PromptFavorite` declares `HasKey(f => new { f.UserPartitionKey, f.PromptId })` and its FK with `OnDelete(DeleteBehavior.Cascade)`.
- Registered with a lazy connection string (`PromptSql:ConnectionString`) so the app boots without a live SQL dependency, matching specs 014/009.

## Requirement traceability

| Requirement | Where satisfied |
|---|---|
| FR-001 / FR-002 | `PromptAccessEvaluator` over `OwnerPartitionKey`, `CollaboratorPartitionKeys`, `SharedWith` + `UserModel.GroupTokens` |
| FR-003 / FR-013 / FR-014 | `PromptValidationRules` on every write path |
| FR-004 | Every `SharedWith` entry evaluated through spec 018's `ISharingPolicyService` |
| FR-005 / FR-006 | Transfer DTO carries only `NewOwnerEmail`; all other fields re-read from the row |
| FR-007 / FR-008 | Single-row `UPDATE`; `Id` immutable; `RowVersion` conflict ⇒ 409 |
| FR-009 | Gated paths return `UNAUTHORIZED` only, never `NOT_FOUND` |
| FR-010 / FR-011 | `PrimaryModelId`/`FallbackModelId` + structured JSON error body |
| FR-012 / FR-018 | `ChatComposerState.SeedFromPrompt` verbatim assignment |
| FR-015 | Row delete + cascade |
| FR-016 / FR-017 / FR-019 | `PromptFavorite` composite key, FK cascade, untouched by transfer |
