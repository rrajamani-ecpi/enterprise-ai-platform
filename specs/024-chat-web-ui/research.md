# Phase 0 Research: Chat Web UI (Stories 1–2 slice)

Scope note: this research covers only User Story 1 (sign-in/chat home) and User Story 2 (start conversation, streaming reply). Stories 3–5 (history/rename, multi-pane compare, changelog) are deferred to a later plan pass and are not researched here.

## Decision: Component calls existing Application-layer services directly, not the HTTP/SSE endpoints

**Decision**: The Blazor component composes `IChatThreadStore`, `IChatPipeline`, `IModelAccessService`, `ICurrentUserAccessor`, and `IIdentityHasher` directly via DI — the same interfaces `ChatEndpoints.cs` already calls — instead of issuing loopback HTTP calls to `/api/chat/threads` / `/api/chat/threads/{id}/messages`.

**Rationale**: The app runs Blazor Web App in **Interactive Server** mode (`Program.cs`: `AddInteractiveServerComponents()` / `AddInteractiveServerRenderMode()`), so component code already executes server-side with a persistent SignalR circuit. Calling the same in-process services the endpoint calls:
- Avoids duplicating `ChatEndpoints.cs`'s thread-creation/send logic in a second place (Constitution Principle IV — one implementation per concern; the endpoint and the component become two callers of one implementation, not two implementations).
- Lets the component `await foreach` the `IAsyncEnumerable<string>` chunks from `ChatSendResult.Streaming` directly into component state + `StateHasChanged()`, which is the natural fit for Interactive Server per the constitution's Technology Stack section — no SSE-over-HttpClient parsing needed.
- Keeps caller identity resolution server-side via `ICurrentUserAccessor.GetCurrentUser()` inside the component, consistent with Principle II — the component never trusts a client-supplied identity.

**Alternatives considered**: Loopback `HttpClient` calls to the existing endpoints (rejected — adds an unnecessary in-process HTTP hop, requires manually forwarding the auth cookie/context and manually parsing the `data: ...` SSE framing that the component doesn't need since it can consume the `IAsyncEnumerable` directly).

## Decision: Default model is resolved client-side as the first entitled model

**Decision**: On first load, the component calls `IModelAccessService.GetAvailableModelsAsync(caller)` and uses the first returned model's Id as the `ModelId` passed to `IChatThreadStore.CreateAsync`.

**Rationale**: `POST /api/chat/threads` requires a `ModelId`, but spec 024's Story 1 (confirmed in Clarifications) presents a ready-to-type composer with no separate model-selection step. Investigation confirmed **no existing "default" or "preferred model" concept**: `IModelAccessService.GetAvailableModelsAsync` returns only the caller's full entitled list (registry order, no default flag), and `UserPreferences` (spec 021, unimplemented) has no `PreferredModelId` field. Picking the first entitled model is a UI-side convenience within the caller's existing entitlements — it adds no new backend capability, consistent with spec 024's Assumptions ("this spec adds a visible surface for what those systems already support, not new backend capability").

**Alternatives considered**: Adding a `PreferredModelId` to `UserPreferences` (rejected — that's spec 021's scope, unimplemented, and out of scope here); adding a minimal model-selector dropdown to the composer (rejected for this pass — not in spec 024's Story 1/2 acceptance criteria; would expand scope beyond the "something to test" MVP goal).

## Decision: No new HTTP contract for Stories 1–2

**Decision**: No new `/api/*` endpoint is introduced. All persistence/streaming continues through the existing spec-004 `IChatThreadStore` / `IChatPipeline` contracts.

**Rationale**: Story 2's acceptance scenario 3 ("active conversation with prior messages" → follow-up message) only requires the *same page session* to retain prior messages in component state — it does not require reloading history from the server (that's Story 3's "select a conversation from the list... prior messages rendered," explicitly deferred). No GET-thread-messages or list/rename endpoint is needed for this slice.

## Decision: Unauthenticated routing is already satisfied by existing middleware

**Decision**: No new authorization code is needed for FR-001's "route unauthenticated visitor into sign-in flow" requirement.

**Rationale**: `Program.cs` registers `MapRazorComponents<App>().AddInteractiveServerRenderMode()` with no `AllowAnonymous()`, so the global fallback policy (`SetFallbackPolicy(RequireAuthenticatedUser)`) already applies to it. `UseAuthentication()`/`UseAuthorization()` run ahead of component rendering, so an unauthenticated request is already challenged into the configured scheme (Entra OIDC in production, `DevelopmentAuthenticationHandler` in dev) before any chat content renders. This was verified by reading `Program.cs` directly, not assumed.

## Decision: Test coverage extends existing test projects; no new test project

**Decision**: New logic is unit-tested in `EnterpriseAIPlatform.UnitTests` (NSubstitute, mirroring `ChatPipelineTests.cs`), and the unauthenticated-redirect behavior is verified in `EnterpriseAIPlatform.IntegrationTests` (`WebApplicationFactory`, mirroring `ChatSendMessageTests.cs`). A new `bUnit` package dependency is added to `EnterpriseAIPlatform.UnitTests` for component-render assertions (e.g., composer disabled while streaming) — this is the first Blazor UI feature in the repo, and bUnit is the standard component-test library for Blazor, not a second implementation of an existing test capability.

**Rationale**: The constitution's Complexity Tracking guidance flags unjustified *new projects* (e.g., "4th project" in its own worked example) — adding a 4th test project here would need that same justification and isn't warranted for one feature's component tests. Extending the existing `UnitTests` project with one new package dependency is the smaller change.

**Alternatives considered**: A dedicated `EnterpriseAIPlatform.ComponentTests` project (rejected — unjustified new project for this scope); Playwright E2E (rejected — no existing Playwright setup in the repo, and out of scope for an MVP slice; can be reconsidered when Stories 3–5 land).
