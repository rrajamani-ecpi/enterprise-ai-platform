# Phase 0 Research: Persona CRUD & Authorization

**Feature**: 009-persona-crud-authorization | **Date**: 2026-08-31

The three clarifications resolved during `/speckit.clarify` (concurrent-edit blocking, apiKey scope, timing side-channel) are not re-litigated here — they're already in spec.md's Clarifications section. This file covers the *how*.

---

## D1: Storage — Azure SQL via EF Core, not Cosmos (dissolves the "atomic transfer" problem)

**Decision**: `PersonaModel` is a new `PersonaDbContext` entity, Azure SQL via EF Core, following `ModelAccessDbContext`'s exact pattern (lazy connection string, `List<string>`-shaped fields stored via JSON `ValueConverter`/`ValueComparer`, own `Migrations` folder).

**Rationale**: The constitution's Data & Storage section is explicit: "Azure SQL Database (via EF Core) is preferred over Cosmos DB for strongly relational, schema-stable entities (**personas**, prompts, sharing policy, admin/system config)." Spec.md's own bug narrative (`EnsurePersonaOperation`, Cosmos partition-key-based ownership transfer) describes the *legacy* accelerator, not a target this plan is obligated to preserve. On Azure SQL, ownership transfer is a single-row `UPDATE OwnerUserId/OwnerPartitionKey WHERE Id = @id` inside one `SaveChangesAsync()` — already atomic, with **no delete-then-recreate step at all**. This means:
- FR-001 ("never lost or duplicated") holds by construction — there is no intermediate state where the row doesn't exist, because the row's primary key (`Id`) never changes, only a column on it does.
- FR-002 ("safely retryable") holds by construction — a retry either finds the precondition (`WHERE OwnerUserId = @expectedCurrentOwner`) still true (proceeds normally) or already-changed (reports "already transferred," a harmless no-op, never a duplicate).
- Edge Case ("must not create duplicate personas under both owners") cannot happen — there is no "recreate" step to duplicate.

**Alternatives considered**: Building the two-phase saga (mark-pending → copy → verify → delete-old) the spec's Key Entities section describes, on Cosmos, matching the legacy mechanics exactly (rejected — the constitution already directs new relational entities to Azure SQL, and building a saga to solve a problem SQL doesn't have would be unjustified complexity, contrary to the "don't add abstractions beyond what the task requires" default).

---

## D2: Hashed owner/collaborator identity — reuse `IIdentityHasher.ForEmail`

**Decision**: `PersonaModel.OwnerPartitionKey` and every entry in the hashed-collaborators list are produced by spec 002's existing `IIdentityHasher.ForEmail(string email) → StoragePartitionKey` (SHA-256 over the lowercased/trimmed email).

**Rationale**: This is the one and only identity-hashing utility in the codebase (`src/EnterpriseAIPlatform.Infrastructure/Identity/IdentityHasher.cs`), already used for `ChatThreadModel.PartitionKey`/`OwnerUserId`. Constitution Principle IV forbids a second, divergent hashing implementation for personas.

**Alternatives considered**: A persona-specific hasher (rejected — no reason exists for personas to hash differently than threads; would create the exact "second implementation" Principle IV flags as a defect).

---

## D3: Central access gate — `PersonaAccessEvaluator`, a new static evaluator mirroring `SharingPolicyEvaluator`

**Decision**: `PersonaAccessEvaluator` (Application layer, static, DI-free) implements FR-003 through FR-005: admin/owner/hashed-collaborator get full access; a student gets read-only when `IsLessonPersona == true`; everyone else gets denied. A thin `PersonaService : IPersonaService` (Infrastructure) resolves `ICurrentUserAccessor`'s `UserModel` and delegates to it.

**Rationale**: No shared "EnsureXOperation"-style abstraction exists yet in this codebase — `IChatThreadStore`'s access control is baked ad hoc into each query's partition-key filter, and the one real *shared, reusable* decision function that exists is spec 018's `SharingPolicyEvaluator` (static, pure, `Evaluate(...) → Decision`). This spec is the first persona-specific instance of that same shape — introduced deliberately so it survives refactors as a single, explicit, testable contract (spec's own Story 2 rationale), and so the combinatorial role × ownership × lesson-persona matrix (SC-002/SC-003) is unit-testable with zero infrastructure.

**Alternatives considered**: Baking the check into each `IPersonaService` method ad hoc, matching `IChatThreadStore`'s existing style (rejected — spec 009 explicitly asks for this to be "locked in as an explicit, testable contract," which a scattered per-method check can't provide as cleanly, and the FR-003–FR-005 combination is more branchy than a single partition-key filter).

---

## D4: Enumeration prevention — `Unauthorized` only, fixed message, never `NotFound`

**Decision**: `PersonaAccessEvaluator`-gated operations return **only** `ServerActionResponse<T>.Unauthorized("You do not have access to this persona.")` — the exact same `Status` and message — for both "persona doesn't exist" and "persona exists but caller lacks access." `NotFound` is never returned from a gated read/write path.

**Rationale**: Direct inspection of `ServerActionResponse<T>` (`src/EnterpriseAIPlatform.Application/Common/ServerActionResponse.cs`) shows `NotFound` and `Unauthorized` set *different* `Status` enum values (`NOT_FOUND` vs. `UNAUTHORIZED`). Returning `NotFound` for "doesn't exist" and `Unauthorized` for "forbidden" — the naive, seemingly-obvious implementation — would itself be the distinguishing signal FR-004/SC-002 explicitly forbids ("indistinguishable from not found... 0 distinguishable signal"). The per-clarification decision that response-*shape* identity (not timing) is sufficient makes this tractable: match `Status` and `Errors[0].Message` exactly, and shape-identity is achieved.

**Alternatives considered**: A new `ResponseStatus.NOT_FOUND_OR_UNAUTHORIZED` merged value (rejected — churns the shared, cross-feature `ServerActionResponse<T>` envelope for one feature's needs); returning `NotFound` for both (rejected — `NotFound`'s existing call sites elsewhere in the app rely on it meaning "genuinely absent," and repurposing it for "forbidden" too would be confusing and still requires the same fixed-message discipline, with no actual benefit over reusing `Unauthorized`).

---

## D5: `apiKey` structural exclusion — a separate `PersonaPublicDTO` type, not `[JsonIgnore]`

**Decision**: `PersonaPublicDTO` is a distinct record type with no `ApiKey` property at all. Every Web-layer/Blazor-component-reachable code path returns `PersonaPublicDTO`, never `PersonaModel`. A separate, `internal`-visibility `IPersonaRawAccessor.GetRawAsync` — not on the public `IPersonaService` at all, compiler-restricted via `InternalsVisibleTo` rather than a source-scan convention — remains available for the legitimate A2A credential-comparison caller (spec 011).

**Rationale**: Blazor Interactive Server components (`src/EnterpriseAIPlatform.Web/Components/**/*.razor`) never round-trip through JSON — they `@inject` scoped "State" services (`src/EnterpriseAIPlatform.Web/Services/`) that hold direct .NET object references from Application-layer calls, serialized to the browser only as SignalR render-diffs. A `[JsonIgnore]` attribute on `PersonaModel.ApiKey` would do nothing to protect this path, since `System.Text.Json` is never in the loop. This is confirmed by the per-clarification decision that FR-009/SC-006 must cover Blazor component state, not just JSON API responses. This is the first instance of a genuine "two record types, one with the secret / one without" split in the codebase — the closest precedent (`AzureFoundryOptions`) instead avoids the problem by never declaring the secret field on the options type at all, which isn't available here since `PersonaModel` legitimately needs `ApiKey` for its one real server-side consumer (A2A comparison, spec 011).

**Alternatives considered**: `[JsonIgnore]` / a runtime "strip" function called per call site (rejected — this is exactly the "opt-in per call site" pattern spec.md's own Story 3 rationale identifies as the root cause of the current gap, and it wouldn't work for Blazor component state regardless).

---

## D6: `dataProducts` conditional-required validation — a static rules class, not DataAnnotations

**Decision**: `PersonaExtensionRules.TryValidate(IReadOnlyList<string> extensions, IReadOnlyList<string> dataProducts, out string? error)` — a static, framework-free method — is called from every create/update entry point (the `IPersonaService` implementation, before any write).

**Rationale**: No FluentValidation, `IValidatableObject`, or DataAnnotations validator exists anywhere in `src/`; the established convention for exactly this shape of rule ("one shared validation all entry points use") is a static rules class — `ConversationRenameRules`, `MultiChatQuadrantRules` — each documented as "the single implementation... unit-testable without a database... never duplicated in a UI-only check (Principle V)." `PersonaExtensionRules` follows the same shape.

**Alternatives considered**: A DataAnnotations `IValidatableObject` implementation on `PersonaModel` itself (rejected — inconsistent with the codebase's established pattern for this exact kind of cross-field, resource-specific rule; DataAnnotations is used in this codebase only for `IOptions`-bound config classes, not domain entities).

---

## D7: Concurrent edit/delete during a transfer — a self-managed `RowVersion` concurrency token, not a bespoke flag

**Decision**: `PersonaModel.RowVersion` (`Guid`, EF Core `IsConcurrencyToken()`) is a self-managed optimistic-concurrency token — `PersonaService` regenerates it on every successful write, rather than relying on SQL Server's native auto-generated `rowversion` column (`IsRowVersion()`), which the EF Core InMemory provider used in integration tests doesn't emulate (it never auto-bumps the stored value, so a naive two-context race wouldn't be detected). Every write (edit, delete, transfer) is a normal `SaveChangesAsync()` call; EF Core automatically includes the loaded `RowVersion` in its optimistic-concurrency check. If a concurrent write already committed since the caller's read, `SaveChangesAsync()` throws `DbUpdateConcurrencyException`, which `PersonaService` catches and maps to a specific "this persona was modified concurrently (an ownership transfer may be in progress) — reload and retry" error, never a silent overwrite or a queued-then-applied write.

**Rationale**: Because D1 already makes the transfer a single atomic statement, there is no genuine multi-step "in-flight" window for a bespoke `TransferInProgress` boolean to mark — by the time any other request's write could observe the row, the transfer has either fully happened or not happened at all. The real risk the clarification identified is the classic lost-update race (edit reads stale data, transfer commits, edit overwrites the transfer) — which `RowVersion` solves generally, for *any* concurrent write (edit-vs-edit too, not just edit-vs-transfer), using the idiomatic, well-known EF Core mechanism rather than new bespoke state. This directly satisfies the clarification's "rejected... never queued... never allowed to race it" requirement.

**Alternatives considered**: An explicit `TransferInProgress` bool set before the transfer and cleared after (rejected — would require splitting the transfer into two `SaveChangesAsync()` calls, reintroducing the very multi-step-non-atomicity problem D1 eliminated, to solve a race that `RowVersion` already covers in one atomic statement).

---

## D8: Sharing-target role-gating (FR-008) — 009's own binary rule, not spec 018's evaluator

**Decision**: FR-008 ("admins may share with group tokens; non-admins may share only with individuals") is implemented as a small, self-contained check inside `PersonaAccessEvaluator` (or a sibling static method) — it does **not** call spec 018's `ISharingPolicyService`.

**Rationale**: Spec 009's own Assumptions section states this explicitly: 018's three-tier policy "supersedes 009's binary admin/non-admin model as the target behavior; 009 itself is not modified here [by 018]" and that 009 "should eventually consume 018's `SharingDecision`... out of scope here." Wiring 018 in now would be scope creep this spec's own text disclaims.

**Alternatives considered**: Calling `ISharingPolicyService` now, since it already exists in the codebase (rejected — 009's spec text explicitly defers this integration to a future refactor; doing it now without a spec update would build ahead of what's actually specified, and would need to reconcile 018's three-tier role model with 009's binary one, which is exactly the follow-up work 009's Assumptions flags as separate).

**Addendum (post-`/speckit.analyze`)**: FR-008's "documented company-wide overrides" clause had no defined mechanism anywhere in the design docs. Resolved via a second clarification pass: no override exists in this build — a non-admin's group-token share attempt is rejected unconditionally. Building a 009-local override config to satisfy the clause literally would duplicate spec 018's `GloballyAllowedGroups` mechanism, which Principle IV forbids; the clause anticipated 009's eventual refactor onto 018, not new 009-local config.

---

## D9: Testing — extend `TestAuthHandler` for role-based integration coverage

**Decision**: `TestAuthHandler` (currently admin/non-admin only, via `X-Test-Admin`) gains a `X-Test-Roles` header (comma-separated `RoleFlags` names) so `Employee`/`Contractor`/`Student` can be simulated over HTTP, matching `ChatConversationListTests`' differing-caller-email pattern for owner-vs-other.

**Rationale**: The existing integration-test harness can already distinguish admin/non-admin and distinct callers by email (for ownership), but has no way to simulate a student caller over HTTP — needed for Story 2's lesson-persona read-only scenario and the full admin/owner/collaborator/student/unrelated test matrix this spec's Independent Tests call for.

**Alternatives considered**: Testing all role combinations only at the unit level against `PersonaAccessEvaluator` directly, skipping HTTP-level role coverage (rejected — Story 2's Independent Test explicitly calls for exercising this "directly via API," and FR-006's UI/API-parity concern for sharing, plus general defense against route-level regressions, is best covered by at least one real HTTP pass per role).

---

## D10: Endpoint style — minimal API, manual in-handler gate, admin-policy for admin-only routes

**Decision**: `PersonaEndpoints.MapPersonaEndpoints(this IEndpointRouteBuilder app)` — minimal API, plain `sealed record` DTOs at the bottom of the file, matching `ChatEndpoints`/`ModelAccessEndpoints`. Ownership-transfer's admin-only route uses `.RequireAuthorization(PolicyNames.RequireAdmin)` (route-declarable, like `ModelAccessEndpoints`'s admin writes); read/edit/delete routes call `PersonaAccessEvaluator` manually in-handler (not route-declarable, since ownership/collaborator status isn't a static policy) — matching `ChatEndpoints`' manual-check style.

**Rationale**: No controllers exist anywhere in this codebase; minimal API is the only convention. Admin-only gating is route-declarable via the existing `RequireAdmin` policy; ownership/collaborator/lesson-persona gating inherently is not (it depends on the specific resource and caller), so it must be an in-handler check — exactly the split already present between `ModelAccessEndpoints` (policy-based, admin-only writes) and `ChatEndpoints` (manual, ownership-based reads/writes).

---

**All Technical Context items resolved; no `NEEDS CLARIFICATION` markers remain.**
