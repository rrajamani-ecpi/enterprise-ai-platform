# Quickstart: Prompt CRUD, Sharing & Ownership Transfer

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Phase**: 1

How to run and validate this feature end-to-end. Every success criterion SC-001…SC-008 maps to a named, runnable test below.

---

## Prerequisites

Same as specs 014/009 — no new tooling:

- **.NET 10 SDK**
- The existing solution restores without new packages (`dotnet restore EnterpriseAIPlatform.slnx`)
- No live Azure SQL instance is required for tests: `PromptDbContext` is registered with a lazy connection string and integration tests substitute the EF Core InMemory provider
- To run the app against real storage, set `PromptSql:ConnectionString` (user-secrets locally, Key Vault-backed app setting in Azure) and apply migrations:

```bash
dotnet ef database update --context PromptDbContext --project src/EnterpriseAIPlatform.Infrastructure --startup-project src/EnterpriseAIPlatform.Infrastructure
dotnet ef database update --context ModelAccessDbContext --project src/EnterpriseAIPlatform.Infrastructure --startup-project src/EnterpriseAIPlatform.Infrastructure
```

The Infrastructure project is its own startup project here: it owns the design-time `IDesignTimeDbContextFactory` implementations, and `EnterpriseAIPlatform.Web` does not reference `Microsoft.EntityFrameworkCore.Design` (passing `--startup-project src/EnterpriseAIPlatform.Web` fails for that reason).

The second command applies the additive `PrimaryModelId`/`FallbackModelId` columns to spec 014's existing context ([data-model.md](./data-model.md)).

---

## Build & test

```bash
dotnet build EnterpriseAIPlatform.slnx
dotnet test EnterpriseAIPlatform.slnx
```

**Expected**: solution builds clean, and the full suite passes — including every pre-existing test from specs 002/004/006/014/017/018/024/009 **unmodified**. That last point is a hard acceptance condition, not a nicety: this feature edits `UserModel`, `RoleClaimsTransformation`, `PersonaGenerationModelConfig`, `ChatComposerState`, and `SidebarNav`, all owned by earlier merged specs. If a prior spec's test had to be changed to make this one pass, the change was not additive and must be revisited ([research.md D6](./research.md)).

Targeted runs while iterating:

```bash
dotnet test tests/EnterpriseAIPlatform.UnitTests --filter "FullyQualifiedName~Prompts"
dotnet test tests/EnterpriseAIPlatform.IntegrationTests --filter "FullyQualifiedName~Prompts"
dotnet test tests/EnterpriseAIPlatform.ArchitectureTests
```

---

## Success-criteria validation

| SC | What it proves | How it is validated |
|---|---|---|
| **SC-001** | Forged fields in a transfer payload never overwrite stored values | `PromptTransferFieldInjectionTests` — a corpus of transfer bodies carrying forged `name`/`description`/`createdAt`/`sharedWith`/`ownerUserId`. After each, re-read the prompt and assert every non-ownership field equals its pre-transfer value. Passes structurally: the DTO has no such properties ([authorization-policies.md](./contracts/authorization-policies.md)). |
| **SC-002** | A failed transfer never loses the prompt | `PromptTransferAtomicityTests` — force `SaveChangesAsync` to fail mid-transfer, then assert the row still exists, unchanged, under the **original** owner. The spec's "recreate step" does not exist here: transfer is one `UPDATE` with an immutable `Id`, so zero-or-two-copies is unreachable (FR-007/FR-008). |
| **SC-003** | Total generation failure returns JSON, not plain text | `PromptGenerationFailureTests` — a matrix of {primary fails, no fallback}, {primary fails, fallback fails}. Assert `Content-Type: application/json` matching the success path, a structured error body, and **no** success-shaped response carrying an error string as generated content (Principle III, FR-011). |
| **SC-004** | No unauthorized write ever succeeds | `PromptAccessEvaluatorTests` (unit, DI-free) over {owner, admin, collaborator, individual-share, group-share, unrelated} × {read, write, transfer}; plus `PromptWriteAuthorizationTests` (integration) asserting share-target and unrelated callers get 401 on `PATCH`/`DELETE` and that the stored row is byte-identical afterwards. |
| **SC-005** | Authorized CRUD round-trips correctly | `PromptCrudTests` — create → read → update → read → delete → read, run as owner, as admin, and as collaborator. Each mutation must be visible on the next read. Also covers FR-003 rejection of empty `name`/`description` and the `RowVersion` 409 path. |
| **SC-006** | Delete leaves nothing behind | `PromptDeleteCascadeTests` — favorite a prompt from three different users, delete it, then assert it is absent from every list, every read, and **every user's** favorites, with zero `PromptFavorite` rows remaining. The cleanup comes from the FK cascade, so the test also guards the mapping, not just the handler (FR-017). |
| **SC-007** | Favorites are strictly per-user | `PromptFavoritesTests` — user A favorites a prompt; assert it appears in A's list and **not** in B's. Repeat-favorite is idempotent (composite PK), unfavorite is idempotent, and favoriting a prompt the caller cannot read returns 401 (FR-016). Also asserts a transfer leaves all favorites untouched (FR-019). |
| **SC-008** | Prompt selection seeds the composer verbatim | `ChatComposerStateSeedTests` — `SeedFromPrompt` assigns `Description` with zero substitution, including prompts containing `{placeholder}`-looking text, braces, and newlines, which must survive unchanged (FR-012/FR-018). |

**Result (last full run)**: 418/418 passing — 30 architecture, 249 unit, 139 integration — against a 295-test pre-implementation baseline, with every pre-existing test file changed only by append (`TestAuthHandler`, `RoleClaimsTransformationTests`) and zero deletions.

---

## Manual walkthrough

```bash
dotnet run --project src/EnterpriseAIPlatform.Web
```

1. Sign in with Entra ID and open **Prompts** in the sidebar.
2. **Create** a prompt with a name and description → it appears in the library. Submitting an empty name or description is rejected (FR-003).
3. **Share** it with an individual and a group. Invalid targets are refused with the reason returned by spec 018's `ISharingPolicyService` (FR-004).
4. Sign in as the sharee → the prompt is **readable but not editable**; edit and delete controls are absent *and* the API rejects them directly with 401 (FR-002, Principle V).
5. **Favorite** it, then confirm it appears only in your own favorites (FR-016).
6. **Select** the prompt → the chat composer is populated with the description verbatim; edit and send through the normal chat flow (FR-012).
7. As the owner, **transfer** ownership. The prompt keeps its id, all fields, and everyone's favorites; the new owner can now edit it and you cannot (FR-005–FR-008, FR-019).
8. **Delete** it as the new owner → it vanishes from every list and every favorites view (FR-015/FR-017).

## Non-revealing-error spot check

```bash
# an existing prompt you have no access to
curl -i https://localhost:5001/api/prompts/{someone-elses-id}
# an id that has never existed
curl -i https://localhost:5001/api/prompts/does-not-exist-000
```

**Expected**: byte-identical `401` responses with the same JSON body. Any difference in status code, message, or headers between the two is an FR-009 failure — see [authorization-policies.md](./contracts/authorization-policies.md).

## References

- [data-model.md](./data-model.md) — entity shapes, EF mapping notes, requirement traceability
- [contracts/](./contracts/) — service interfaces, route table, authorization policies
- [research.md](./research.md) — decisions D1–D11, including the `UserModel.GroupTokens` cross-spec change
