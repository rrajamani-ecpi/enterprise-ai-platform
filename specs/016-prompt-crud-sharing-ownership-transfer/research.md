# Research: Prompt CRUD, Sharing & Ownership Transfer

**Spec**: [spec.md](./spec.md) | **Date**: 2026-08-31 | **Phase**: 0

Decisions resolving the Technical Context unknowns for spec 016. Each records what was chosen, why, and what was rejected. Five of these were pre-resolved by the `/speckit-clarify` session recorded in spec.md's Clarifications section; they are restated here with their implementation consequences.

---

## D1 — Storage: Azure SQL via EF Core, not Cosmos DB

**Decision**: `PromptModel` and `PromptFavorite` persist in Azure SQL through a new `PromptDbContext`, mirroring spec 009's `PersonaDbContext` exactly (own `Migrations/` folder, lazy connection string, JSON `ValueConverter` + `ValueComparer` for list-shaped columns).

**Rationale**: The constitution's Data & Storage section names prompts explicitly — "Azure SQL Database (via EF Core) is preferred over Cosmos DB for strongly relational, schema-stable entities (personas, **prompts**, sharing policy, admin/system config)". Spec 009 already made the identical call against an identical Cosmos-flavoured bug narrative and shipped it.

**Consequence — this is what makes US2 nearly free**: the spec's "atomic or recoverable" requirement (FR-007) is framed against a Cosmos partition-key move, where `userId` is the partition key and changing the owner therefore *requires* a cross-partition write-then-delete. In SQL the owner is an ordinary column: transfer is a single-row `UPDATE` inside one `SaveChangesAsync()`, atomic by construction. `Id` never changes, so there is no window in which zero or two copies of a prompt can exist. FR-007 and FR-008 hold structurally rather than by careful sequencing.

**Alternatives rejected**:
- *Cosmos DB with a write-then-delete compensating action* — preserves the legacy failure mode as a thing to defend against, contradicts the constitution, and would make this the only non-relational entity among personas/prompts/sharing/config.
- *Extend `PersonaDbContext` to hold prompts* — merges two unrelated aggregates behind one migration history; spec 009's own factory-isolation bug (see D9) shows the cost of contexts that own more than their concern.

---

## D2 — `PromptAccessEvaluator`: a static, pure Application-layer gate

**Decision**: A new `public static class PromptAccessEvaluator` in `EnterpriseAIPlatform.Application.Prompts`, implementing FR-001 (write: admin / owner / collaborator) and FR-002 (read: additionally anyone in `SharedWith`). Takes the caller's precomputed hashed identity as a parameter rather than depending on `IIdentityHasher` — keeping it DI-free and unit-testable with no infrastructure.

**Rationale**: This is the established convention for every access/policy decision in this codebase — `PersonaAccessEvaluator` (009), `SharingPolicyEvaluator` (018), `ModelAccessEvaluator` (014), `MultiChatQuadrantRules` (006). Principle IV wants exactly one implementation per concern, and the static-function shape is what makes the full role × ownership × share-target matrix (SC-004) testable without a `WebApplicationFactory`.

**Alternatives rejected**: an injectable `IPromptAccessService` (no other gate in the codebase is injectable, and DI buys nothing for a pure function); reusing `PersonaAccessEvaluator` (different rule set — prompts have no lesson-persona or student-read concept, and personas have no `sharedWith`-grants-read rule).

---

## D3 — Enumeration prevention: `Unauthorized` only, never `NotFound`

**Decision**: Every prompt read/write gated by `PromptAccessEvaluator` returns `ServerActionResponse<T>.Unauthorized(...)` with one fixed message for both "no such prompt" and "exists but forbidden". `NotFound` is never returned from a gated path.

**Rationale**: FR-009 requires a uniform ambiguous result. `ResponseStatus.NotFound` and `ResponseStatus.UNAUTHORIZED` map to different HTTP status codes (404 vs 401), so returning `NotFound` for the non-existent case would itself be the distinguishing signal FR-009 forbids — the naive implementation defeats the requirement. Spec 009 hit and solved the same trap (its research D4); SC-002's equivalent there was resolved as *response-shape identity*, no timing-equivalence measurement required.

**Consequence**: `PromptService` cannot use `NotFound` on any caller-facing method. The one exception is an internal/raw accessor path with no caller identity to gate against, matching `IPersonaRawAccessor.GetRawAsync`.

---

## D4 — Ownership transfer: field injection is prevented structurally, not by validation

**Decision**: The transfer endpoint accepts a request body containing **only** the new owner identifier (`TransferOwnershipRequest(string NewOwnerEmail)`) — the same shape spec 009 uses. `PromptService.TransferOwnershipAsync` re-reads the prompt from the database and mutates only `OwnerUserId`/`OwnerPartitionKey`/`UpdatedAtUtc`/`RowVersion`.

**Rationale**: FR-005 says the transfer MUST re-derive `name`/`description`/`createdAt`/`sharedWith` from the server record and discard client-supplied values. The strongest form of "discard" is *never bind them in the first place*: there is no DTO property for an attacker to populate, so US1's acceptance scenarios pass by construction rather than by a validation step someone can later forget. This mirrors the constitution's guidance on `apiKey` exclusion — protection should be structural at the type level, not conventional at each call site.

**Consequence for testing**: SC-001's "corpus of forged-field variations" is tested by posting supersets of the expected JSON body and asserting the persisted record is unchanged — ASP.NET Core's model binder ignores unknown properties, so the forged fields are dropped before any handler code runs.

**Alternatives rejected**: accepting a full `PromptWriteRequest` on transfer and overwriting the client's values server-side — functionally equivalent when written correctly, but it keeps a live injection surface that only a code review can prove safe.

---

## D5 — Sharing: consume spec 018's `ISharingPolicyService`

**Decision**: `PromptService` injects `ISharingPolicyService` and calls `Evaluate(caller, new ShareTargetRequest(type, groupToken))` for **every** share target on create and update, rejecting the write if any target's `SharingDecision.IsAllowed` is false. No prompt-local role rules.

**Rationale**: Recorded in spec.md Clarifications. Spec 018 shipped `SharingPolicyEvaluator` with zero consumers; its own XML doc states it is "what specs 009/012/016 are meant to consume instead of re-implementing their own share-target validity checks (Constitution Principle IV)". Spec 009 deferred adoption and wrote a local binary admin/non-admin rule (`PersonaAccessEvaluator.CanShareGroupTarget`) — a second implementation that Principle IV tolerates only until the first spec retires it. 016 is that spec.

**Consequence**: prompts get the full policy — `AdminOnlyMode`, `DisableAllGroupSharing`, `GloballyAllowedGroups`, and per-role `AllowedGroups` — rather than persona's binary rule, at no extra cost. This also gives 018's evaluator its first integration-level test coverage.

**Out of scope**: refactoring `PersonaAccessEvaluator.CanShareGroupTarget` onto the same service. It is a behaviour change to a merged spec and belongs in its own change.

---

## D6 — Group-token read access requires retaining the `groups` claim *(the one cross-spec ripple)*

**Problem discovered during planning**: FR-002 grants read access to "anyone the prompt is `sharedWith` (individual email **or group token**)". The individual half is straightforward — compare the caller's hashed identity against the stored target. The group half is **not currently implementable**: `UserModel` (spec 002) carries `Name`, `Email`, `Image`, `RoleFlags`, and two booleans — no group membership. `RoleClaimsTransformation` *does* read the Entra `groups` claim, but only passes the GUIDs to `IRoleResolver.DeriveFrom(...)` to compute role flags and then discards them.

**Decision**: Extend `UserModel` with `IReadOnlyList<string> GroupTokens` (default empty) and populate it in `RoleClaimsTransformation` from the same `groups` claim it already reads. `PromptAccessEvaluator` then matches a `Group` share target against that list.

**Rationale**: The claim is already available at exactly the point the transformation runs — no new token acquisition, no extra Entra round-trip, no new configuration. Extending the one canonical session projection is what Principle IV prefers over introducing a parallel `ICallerGroupAccessor` that would become a second source of truth for "who is this caller". The addition is purely additive and defaults to empty, so every existing `UserModel` construction site (tests included) keeps compiling.

**Risk flagged**: this touches spec 002's walking skeleton, which everything depends on. Mitigation: additive optional property with a safe default; spec 002's existing tests must stay green unmodified, which is an explicit task acceptance condition.

**Alternatives rejected**:
- *Defer group-token reads; support individual share targets only* — leaves FR-002 half-implemented and SC-004 untestable for the group case. Viable fallback if the `UserModel` change proves contentious, but it ships a knowingly incomplete requirement.
- *Read `HttpContext` claims inside `PromptService`* — pushes identity parsing into a data service, breaks the DI-free evaluator, and re-derives identity outside spec 002's single canonical path (Principle II's "derived server-side from the verified session" is satisfied either way, but Principle IV is not).

---

## D7 — Favorites: a separate entity with a database-level cascade

**Decision**: `PromptFavorite` is its own EF Core entity with a composite key `(UserPartitionKey, PromptId)` and a required FK to `PromptModel` configured `OnDelete(DeleteBehavior.Cascade)`.

**Rationale**: Recorded in spec.md Clarifications. Principle V requires rules that gate persisted data to live in the schema layer, not at call sites — a cascade makes SC-006's "0 dangling references" a property of the database rather than a property of every future delete path remembering to clean up. The composite key makes double-favoriting idempotent-by-constraint and makes one user's favorites structurally incapable of appearing in another's (SC-007).

**Note on the spec's Key Entities**: spec.md described `PromptFavorite` as `userId` + `promptIds[]` (a per-user list document, mirroring the legacy Cosmos shape). Under D1 that denormalised array cannot carry a foreign key and would defeat the cascade, so the relational form is one row per (user, prompt). The spec's Key Entities section was updated accordingly during clarification.

**Alternatives rejected**: filter-on-read (leaves orphans accumulating and makes SC-006 a query concern that every read path must remember); application-level cleanup in the delete transaction (correct but call-site-dependent, exactly what Principle V rules out).

---

## D8 — Prompt generation: extend spec 014's config; accumulate the existing streaming client

**Decision (config)**: Add `PrimaryModelId` and `FallbackModelId` to spec 014's existing `PersonaGenerationModelConfig` singleton, with `AllowedModelIds` retained as the validity set both must belong to. One additive EF Core migration on `ModelAccessDbContext`. No new config entity.

**Rationale**: Recorded in spec.md Clarifications. The entity's own XML doc already scopes it to "AI-assisted persona/**prompt** generation" — 014 anticipated this consumer. A separate `PromptGenerationModelConfig` would be a second implementation of "which models may generate content" (Principle IV). An unordered allow-list alone cannot answer "which is primary", leaving FR-010 unfalsifiable and violating Principle VI.

**Decision (invocation)**: `PromptGenerationService` calls the existing `IChatCompletionClient.StreamCompletionAsync(...)` and accumulates the chunks into a single string, rather than adding a non-streaming completion method.

**Rationale**: `IChatCompletionClient` is the one model-invocation seam (spec 004), and it is streaming-only. Prompt generation returns a single JSON body, so it consumes the whole stream before responding. Adding a parallel non-streaming method to the interface would create a second model-call path for every provider adapter to implement.

**Consequence for FR-011**: the primary attempt is wrapped in a `try`/`catch`; on failure the fallback is attempted once; if both fail the endpoint returns a structured JSON error body with `Content-Type: application/json`, identical in shape to the success path. This is the whole of US3 — the legacy defect was a plain-text 500.

---

## D9 — Test host: a `PromptWebApplicationFactory` with an isolated EF internal service provider

**Decision**: Add `PromptWebApplicationFactory` following `PersonaWebApplicationFactory`'s exact pattern — remove **only** `DbContextOptions<PromptDbContext>` and `PromptDbContext` descriptors, then re-register on EF Core InMemory with its own `UseInternalServiceProvider(...)`.

**Rationale**: This is the pattern spec 009 arrived at after hitting two real failures, documented in `PersonaWebApplicationFactory`'s summary: a blanket "remove every EF-Core-namespaced descriptor" sweep also strips sibling contexts' registrations (breaking `ValidateOnBuild` for unrelated services), and once fixed, EF Core's *shared* internal service provider sees SqlServer (untouched sibling contexts) and InMemory (the swapped one) at once and throws "multiple database providers registered". Adding `PromptDbContext` as the codebase's **third** `DbContext` walks straight into both traps unless the established pattern is copied.

**Consequence**: the existing factories need no modification — each already scopes its removal to its own context type. This is the payoff from 009's fix.

---

## D10 — Concurrency: reuse the self-managed `RowVersion` token

**Decision**: `PromptModel.RowVersion` is a `Guid` marked `IsConcurrencyToken()` (not `IsRowVersion()`), regenerated by `PromptService` on every successful write.

**Rationale**: `IsRowVersion()` binds to SQL Server's native auto-generated `rowversion` column type, which the EF Core InMemory provider used by the integration tests does not emulate identically. The self-managed `Guid` is provider-portable and is precisely what spec 009 landed on for the same reason.

**Consequence**: resolves the spec's double-submit edge case — the second concurrent transfer loses on the concurrency check and gets a `DbUpdateConcurrencyException`, surfaced as a distinct 409 rather than a silent last-write-wins overwrite. The loser retries against the intact record (FR-007's Acceptance Scenario 3).

---

## D11 — US6 chat seeding: extend `ChatComposerState`, no new chat logic

**Decision**: Add a `SeedFromPrompt(string description)` method to the existing `ChatComposerState` (spec 024) that sets `ComposerText` verbatim and raises `OnChanged`. A new `/prompts` Blazor page lists the caller's prompts and navigates to the chat home with the selected prompt's `description` seeded.

**Rationale**: FR-012 requires copying `description` verbatim with **no** placeholder substitution — so this is genuinely an assignment, not a templating feature. `ChatComposerState` already owns composer text for the whole chat surface and is Scoped per circuit; seeding through it reuses the entire existing send path rather than introducing a second one (Principle IV).

**Consequence**: FR-018's "any other entry point" (e.g. a preferences landing action, spec 021) is satisfied by any caller of the same method — no per-entry-point mechanic. `[bracket]`-style text in a description is copied literally, which the spec's Assumptions explicitly designate as intended behaviour, not a bug.

---

## Constitution currency check

The constitution's Technology Stack section carries a standing instruction to re-verify GA status and pin package versions at the first `/speckit-plan` that depends on them. **This spec adds no new package**: EF Core + `Microsoft.EntityFrameworkCore.SqlServer` are already dependencies via specs 014 and 009, and no Microsoft Agent Framework, Foundry A2A, or preview-tier capability is touched. The unpinned-version follow-up therefore remains open against whichever spec first introduces those, not this one.
