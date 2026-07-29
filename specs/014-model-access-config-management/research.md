# Phase 0 Research: Model & Access Configuration Management

**Feature**: 014-model-access-config-management | **Date**: 2026-07-28

This document resolves the technology unknowns for translating the spec's source facts (role-gated model registry, soft-delete, message-limit/persona-gen-model asymmetry, multi-provider adaptation, workload identity) onto the constitution's target stack, scoped to the R1 subset per `docs/Release1-MVP Plan.md`. Format: Decision / Rationale / Alternatives considered.

---

## D1. Storage for admin/system config entities

- **Decision**: **Azure SQL Database via EF Core** for `SystemModelConfig`, `ModelConfigDocument`, `ModelAliasDocument`, `MessageLimitConfig`, `PersonaGenerationModelConfig`.
- **Rationale**: The constitution's Data & Storage guidance names "admin/system config" explicitly as a Azure-SQL/EF-Core case (strongly relational, schema-stable), distinct from spec 002's Cosmos usage for session-scoped documents. EF Core also gives Principle V (schema-enforced validation) a natural home via model configuration/constraints.
- **Alternatives considered**: Cosmos DB (would work, but the constitution's own guidance routes this entity category to SQL; using Cosmos here would fragment the "one implementation per storage concern" pattern); flat config files (fails FR-011's "addable via configuration without a code change" for the catalog, and can't cheaply support admin mutation with validation).

## D2. Cache-with-fallback for system-config reads (FR-014/SC-010)

- **Decision**: **Azure Cache for Redis** (`IDistributedCache` via `Microsoft.Extensions.Caching.StackExchangeRedis`) fronts `SystemModelConfig` reads; on cache miss, read through to SQL and repopulate; if SQL is also unavailable, serve **hardcoded in-code defaults** (a single-role-open, single-model default) rather than erroring.
- **Rationale**: Matches the constitution's named Redis use case (distributed cache) and Principle III's explicit fail-open carve-out for exactly this scenario ("message-limit config reads" is named in the constitution itself). The three-tier fallback (Redis → SQL → hardcoded) is a documented availability policy, not a silent fabrication.
- **Alternatives considered**: In-memory `IMemoryCache` only (doesn't survive multi-instance deployment — spec 003 already flagged this multi-instance cache gap, which is exactly why the constitution names Redis); no fallback (violates FR-014/SC-010 directly).

## D3. Effective model access computation (FR-003, US2)

- **Decision**: A single pure function `ComputeEffectiveAccess(ModelConfigDocument, UserModel) -> bool` = `model.IsEnabled ∧ RoleAllowListed(model, user.Roles) ∧ (!model.RequiresAdvancedModelAccess ∨ user.AdvancedModelAccess)`, living in Application (framework-free), called on every read — never cached per-user, always recomputed from current `UserModel` + current `ModelConfigDocument` state.
- **Rationale**: Reuses spec 002's `UserModel` role flags and `AdvancedModelAccess` directly — no new identity computation. A pure function keeps SC-002's full combinatorial matrix (role × isEnabled × requiresAdvancedModelAccess × advancedModelAccess) cheaply unit-testable without a database.
- **Alternatives considered**: Precomputing/caching a per-user available-models list at login (goes stale the moment an admin changes config; violates "server-side for every access decision, not only on mutation," FR-003).

## D4. Soft delete (FR-002, US2)

- **Decision**: `ModelConfigDocument.IsDeleted` flag; EF Core global query filter excludes soft-deleted models from the *available-for-selection* queries (feeds D3) but an explicit "include deleted" query path exists for historical thread/persona display (Edge Cases: soft-deleted model still referenced by history must still resolve for display).
- **Rationale**: Satisfies "never physically removed" (FR-002/SC-003) while keeping the common-case query (selectable models) simple via the query filter, and the edge case (historical resolution) explicit rather than accidentally excluded.
- **Alternatives considered**: Hard delete + a separate archive table (contradicts FR-002 directly); no query filter, manual `IsDeleted` checks at every call site (violates Principle IV — "one implementation," here of the soft-delete exclusion rule).

## D5. Read/write asymmetry for message-limit and persona-generation-model config (FR-004–007, US3)

- **Decision**: Two endpoints per config type — an open read (any authenticated caller, reusing spec 002's fallback-authenticated-user policy, no `RequireAdmin`) and an admin-gated write (`RequireAdmin`, same as FR-001). Both live behind the same `IMessageLimitConfigService`/`IPersonaGenerationModelConfigService` interface so the asymmetry is declared once, at the endpoint-policy layer, not duplicated per call site.
- **Rationale**: The spec is explicit this is a *documented, intentional* asymmetry (Story 3's "Why this priority"), not a gap — encoding it as "same service, different endpoint policy" keeps it from silently drifting into all-admin-gated (breaking every user's request path) or all-open (breaking write protection).
- **Alternatives considered**: A single endpoint with in-handler admin-or-self branching (harder to audit for "is this read path actually open," the exact class of bug the spec's Edge Cases worry about).

## D6. Server-side re-validation (FR-008/009, US4)

- **Decision**: Validation lives in the Application-layer service methods (`SetMessageLimit(int cap)`, `SetPersonaGenerationModel(string modelId)`), not in a Web-layer DTO attribute alone — `cap` rejected unless integer ≥1; `modelId` rejected unless present in the persona-generation allow-list, checked against the current DB state at write time.
- **Rationale**: Principle V requires schema/service-layer enforcement, not solely client-side/UI validation (there is no UI in R1 at all, which makes this the only enforcement layer that will ever exist for now — a direct API caller must hit the same wall a future UI would).
- **Alternatives considered**: Data-annotation validation only on a Web DTO (works for `cap`'s ≥1 case but can't express "must be in this dynamic, DB-backed allow-list" — needs a service-layer check regardless).

## D7. `/api/user/preferences/*` 401 fix (FR-010, US5)

- **Decision**: Add an explicit `RequireAuthenticatedUser` policy check (reusing spec 002's fallback-authorization pattern) at the route/endpoint level for every preferences route, returning the same structured `UNAUTHORIZED` `ServerActionResponse` shape spec 002 already established for `GetCurrentUser()`, before any internal helper runs.
- **Rationale**: This is greenfield — there is no legacy 500-throwing helper to work around. Building the route-level check correctly from the start costs nothing extra and keeps the response shape consistent with every other route in this domain (spec 002's D4 envelope).
- **Alternatives considered**: Wrapping the existing internal helper's exception in middleware to translate to 401 (works, but reintroduces exactly the "exception as control flow for an expected case" pattern spec 002/Principle III already moved away from).

## D8. Model catalog shape & provider-adapter seam (FR-011/012, US6/US7 — R1 scope)

- **Decision**: `ModelConfigDocument` schema includes the full field set from spec.md's Key Entities (canonical `provider:modelId`, display name, provider, capability flags, access tier, `contextWindowSize`, pricing) regardless of R1/R2 scope — the **schema** is not scoped down, only the **populated data and adapter implementations** are. A single `IModelProviderAdapter` interface (request/response transform + auth) is introduced in Application; R1 ships exactly one implementation, `AzureFoundryProviderAdapter`; other providers (Claude, Vertex, DeepSeek, Llama, Mistral, Kimi) get their own adapter classes in R2 without touching the interface or any R1 code.
- **Rationale**: Matches `docs/Release1-MVP Plan.md`'s explicit call to defer "multi-provider breadth," while satisfying FR-011's "addable via configuration without a code change" for the *catalog* (new adapters are new classes, not new catalog schema) and avoiding a costly re-design when R2 lands. The MVP doc's Further Considerations also recommends a single approved model in R1, which this directly supports.
- **Alternatives considered**: Deferring the interface itself until R2 (would force a breaking change to every R1 caller of the catalog/access-computation code once multi-provider lands); building all provider adapters now (directly contradicts the R1 scope decision and wastes effort on providers with no R1 caller).

## D9. Workload-identity model-provider authentication (FR-013, US8)

- **Decision**: `AzureFoundryProviderAdapter` authenticates via `DefaultAzureCredential`/workload identity (same `Azure.Identity` dependency spec 002 already introduced for Azure resource auth), never a static API key. No provider secret is placed in `appsettings.json`, environment variables reachable by client code, or any client-delivered payload.
- **Rationale**: Direct application of FR-013 and the constitution's Security & Compliance Constraint that credentials/API keys be excluded from any client-facing accessor **structurally**. Reuses spec 002's existing workload-identity plumbing rather than introducing a second auth mechanism (Principle IV).
- **Alternatives considered**: A rotated API key stored in Key Vault (the spec's own Edge Cases name this as the documented fallback *for providers that don't support managed identity* — not needed for the R1 Azure/Foundry-only scope, so deferred until a non-managed-identity provider is actually added in R2).

## D10. Testing strategy (SC-001…SC-010, R1 subset)

- **Decision**: xUnit unit tests for the D3 access-computation matrix and D6 validation; `WebApplicationFactory` integration tests (reusing spec 002's `CustomWebApplicationFactory`/`TestAuthHandler`) for admin-gate enforcement, soft-delete persistence, read/write asymmetry, and the preferences 401; an architecture test asserting exactly one `IModelProviderAdapter` implementation is registered per provider and exactly one effective-access computation exists. EF Core tests run against SQLite in-memory to avoid a live Azure SQL dependency in CI.
- **Rationale**: Directly maps SC-001–SC-006 (R1-relevant) to concrete tests; SC-007–SC-010 (multi-provider catalog breadth, provider adaptation, workload identity, cache fallback) get single-provider-scoped tests in R1 (e.g., SC-009 tested against the one Azure/Foundry adapter) with the full multi-provider matrix deferred to R2 alongside the adapters themselves.
- **Alternatives considered**: Deferring all SC-007–SC-010 tests entirely until R2 (unnecessary — SC-009's "workload identity, no secrets" claim is fully testable against the single R1 provider today).

---

**All NEEDS CLARIFICATION resolved for the R1 scope.** Deferred-to-R2 items (remaining provider adapters, admin config UI, full multi-provider catalog population) are scope decisions recorded in plan.md's Summary, not open unknowns blocking Phase 1.
