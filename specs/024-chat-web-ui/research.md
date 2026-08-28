# Phase 0 Research: Chat Web UI (Stories 1–3)

Scope note: this research originally covered only User Story 1 (sign-in/chat home) and User Story 2 (start conversation, streaming reply). The "Story 3" section below was added in a follow-on pass covering User Story 3 (conversation list, switch, rename). The "Stories 4–5" section was added in a second follow-on pass covering User Story 4 (multi-pane comparison) and User Story 5 (changelog + version-update banner).

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

## Story 3: Conversation List, Switch, Rename

### Decision: List/rename are new backend capabilities — confirmed no existing precedent

**Decision**: Add `ListByOwnerAsync`, `RenameAsync`, `TouchLastActivityAsync` to `IChatThreadStore` and `ListByThreadAsync` to `IChatMessageStore` — these don't exist today in any form.

**Rationale**: Direct code search confirmed `IChatThreadStore` has only `GetAsync`(by id)/`CreateAsync`; `IChatMessageStore` has only `AppendAsync`. This matches spec 024's own Assumptions ("a conversation currently can only be created and fetched by an id already known to the caller").

### Decision: Cosmos query via `GetItemQueryIterator` + `QueryRequestOptions.PartitionKey`, sorted client-side

**Decision**: `ListByOwnerAsync`/`ListByThreadAsync` issue a single-partition query (`WHERE PartitionKey = @pk AND Type = @type`, with `QueryRequestOptions.PartitionKey` also set so the query cannot fan out across partitions), then sort the results in memory (`ListByOwnerAsync` by `LastActivityAtUtc` descending per the Story 3 clarification; `ListByThreadAsync` by `CreatedAtUtc` ascending).

**Rationale**: This repo has **zero existing precedent** for any cross-document Cosmos query anywhere in `src/` (confirmed by grepping for `GetItemQueryIterator`, `.Query<`, `QueryDefinition`, `GetItemLinqQueryable` — no hits) and **no code-managed Cosmos indexing policy** to verify whether a composite index over `(PartitionKey, Type, LastActivityAtUtc)` exists for a server-side `ORDER BY`. Sorting client-side avoids introducing a dependency on an unverified index, and is safe at this scale — per-user conversation counts and per-thread message counts are both small for an internal pilot (spec 024's existing "limited production pilot, employees-only" assumption).

**Alternatives considered**: Server-side `ORDER BY` in the query (rejected for now — would require confirming/managing a composite index this repo doesn't currently own; revisit if list sizes grow).

### Decision: Rename/touch via ownership-scoped `GetAsync` + `Container.UpsertItemAsync` (read-modify-write)

**Decision**: `RenameAsync` calls the existing `GetAsync(threadId, ownerPartitionKey)` (already ownership-scoped by partition key — a foreign or nonexistent ID both just return `null`), mutates the in-memory model, and writes back via `Container.UpsertItemAsync`. `TouchLastActivityAsync` follows the same shape.

**Rationale**: `CosmosMultiChatSessionStore.SaveAsync` already establishes exactly this read-modify-write-via-`UpsertItemAsync` idiom for "update a chat-adjacent document in place." Reusing it means one update pattern across every Chat Cosmos store (Constitution Principle IV), rather than introducing `PatchItemAsync`/`ReplaceItemAsync` as a second mechanism for these two call sites. This also means FR-013 ("not found," never "forbidden," for a foreign thread) falls out for free — there is no separate ownership-check branch to omit or get wrong; the point read simply returns nothing for a thread that isn't the caller's.

**Alternatives considered**: `Container.PatchItemAsync` with `PatchOperation.Set` (rejected — would be the first patch-style update in the codebase, for no benefit over the existing upsert idiom at this document size/frequency); adding a separate ownership-check step before an unscoped query (rejected — redundant with, and riskier than, the partition-key-scoped point read that already exists).

**Known, accepted gap**: neither this store nor `CosmosMultiChatSessionStore` uses ETags/optimistic concurrency. A rename racing a `TouchLastActivityAsync` on the same thread could theoretically clobber one write with a stale read. This is a pre-existing risk posture, not introduced by Story 3, and isn't fixed here.

### Decision: Rename validation lives in a new Application-layer `ConversationRenameRules`, not the Blazor component

**Decision**: A static `ConversationRenameRules.TryValidate(candidate, out trimmed)` (mirroring the existing `MultiChatQuadrantRules` pattern) rejects empty/whitespace names; `IChatThreadStore.RenameAsync` calls it and returns `ServerActionResponse<ChatThreadModel>` (`OK`/`NotFound`/`Error`).

**Rationale**: Constitution Principle V requires validation in the shared layer, not only in UI call sites, so a direct API caller (via the new `PATCH` endpoint) gets identical enforcement to the Blazor component — not a UI-only check that a direct request could bypass.

### Decision: New HTTP endpoints added for consistency/testability; Blazor UI still calls the stores directly

**Decision**: `GET /api/chat/threads`, `GET /api/chat/threads/{id}/messages`, and `PATCH /api/chat/threads/{id}` are added to `ChatEndpoints.cs`, matching the existing resolve-caller → hash → call-store → map-result convention (mirrors `MultiChatEndpoints.cs`'s `GET /api/multichat/session`). The Blazor `ConversationListState`/`ChatComposerState` classes call `IChatThreadStore`/`IChatMessageStore` directly, not these endpoints.

**Rationale**: Every other spec-004/006 capability (create thread, send message, multichat session) is endpoint-exposed and integration-tested via `WebApplicationFactory`; adding these endpoints keeps list/rename/history consistent with that pattern and gives them the same testability, even though the in-process Blazor UI doesn't need the HTTP hop (same rationale already established for Stories 1–2's send/stream flow).

### Decision: Default conversation name is creation-timestamp-based; list order is most-recently-active-first

**Decision**: `CreateAsync` sets `DisplayName` to a formatted creation timestamp (e.g. `"Conversation — Aug 26, 2026 3:41 PM"`) and `LastActivityAtUtc = CreatedAtUtc`; `ChatPipeline` bumps `LastActivityAtUtc` after every successful send.

**Rationale**: Resolved via `speckit-clarify` (2026-08-26 session) — the spec's Story 3 narrative referenced "its default label" and the Key Entity referenced "last-updated" ordering without defining either; both are now explicit in `spec.md`'s FR-005/FR-007 and Key Entities.

### Decision: `ChatComposerState` gains switch/reset; a new sibling `ConversationListState` owns the list

**Decision**: `ChatComposerState` adds `SwitchToAsync(threadId)` (loads history, sets `IsNotFound` on a bad ID) and `ResetToNewAsync()`. A new `Scoped` `ConversationListState` owns `Conversations`/`LoadConversationsAsync`/`RenameAsync` and calls `ChatComposerState.SwitchToAsync` on selection.

**Rationale**: Keeps single responsibility per class and exactly one owner of "what thread is currently active" — avoids `ChatComposerState`'s already-substantial send/stream logic (and its existing test file) growing to also own list/rename concerns.

**Alternatives considered**: Folding list/rename directly into `ChatComposerState` (rejected — would mix two responsibilities and risk two code paths disagreeing about the active thread).

### Decision: Conversation selection is real URL navigation (`/chat/{ThreadId}`), not in-memory-only state

**Decision**: `/` (`Home.razor`) keeps its Story-1-tested contract unchanged — always a blank, ready-to-type composer. A new `/chat/{ThreadId}` route (`ChatConversation.razor`) is added; both pages host a new `ChatShell.razor` (persistent sidebar). Selecting a conversation calls `NavigationManager.NavigateTo($"/chat/{id}")`.

**Rationale**: Spec 024's own Edge Cases text already describes "navigates directly to a conversation ID that isn't theirs (or doesn't exist)" (FR-013) — this phrasing presumes a real, navigable URL per conversation, not a purely in-memory switch. Bookmarkability/refresh-persistence (AC3's "persists after the page is reloaded") is also only meaningfully testable with a real route. `ChatConversation.razor` is a thin `@page` host for the same reason `Home.razor`→`ChatHome.razor` was split in Stories 1–2: bUnit 2.9.0 cannot mount an `@page`-annotated component directly.

**Not-found handling**: is application-level (rendered by `ChatShell` when `ChatComposerState.IsNotFound` is true), distinct from the router's `NotFoundPage` (which only fires for *unmatched* routes — `/chat/{badId}` is a matched route whose parameter just doesn't resolve to an owned thread).

### Decision: Persistent sidebar layout; inline-edit rename

**Decision**: The conversation list is a persistent sidebar (`ConversationList.razor` inside `ChatShell.razor`), always visible alongside the active composer/transcript. Renaming is inline: click the name in the list, it becomes an editable field in place, Enter/blur saves, Escape cancels.

**Rationale**: Confirmed directly with the user — persistent sidebar over a separate list page (fewer clicks to switch, standard chat-app pattern); inline edit over a modal (faster, no extra UI chrome) for renaming.

## Stories 4–5: Multi-Pane Comparison and Changelog/Version-Alert

### Decision: Zero new backend capability — both stores/dispatchers/readers already exist and are already endpoint-exposed

**Decision**: Reuse spec 006's `IMultiChatSessionStore` / `MultiChatDispatcher` and spec 017's `IChangelogReader` / `IVersionAcknowledgmentStore` / `AlertWindowEvaluator` exactly as they are, calling them directly from new Blazor Server components/state classes — the same "component calls Application-layer services directly" pattern established for Stories 1–3, not the existing `/api/multichat/*` / `/api/changelog*` HTTP routes.

**Rationale**: Direct code inspection confirmed both feature areas are fully implemented, merged, and architecture-tested (`MultiChatSingleImplementationTests.cs`, `SupportSingleImplementationTests.cs`) with zero backend gaps — unlike Story 3, which required new store methods. `CosmosMultiChatSessionStore.GetOrCreateAsync` already seeds a brand-new session with exactly 2 unassigned quadrants (`Position = 0`, `Position = 1`), confirming the 2026-08-28 clarification's "2 unassigned panes by default" answer needs no backend change. `GET /api/changelog/acknowledgment` already computes `showAlert` server-side via `AlertWindowEvaluator.ShouldShowAlert` — the UI's job is only to render that boolean and record a dismissal, not to reimplement the alert-window logic.

**Alternatives considered**: Calling the existing HTTP endpoints via `HttpClient` from the Blazor components (rejected — same rationale as Stories 1–2: an unnecessary in-process HTTP hop when Blazor Server can inject the same services directly, and `MultiChatEndpoints.cs`'s `/api/multichat/session/messages` route streams SSE-framed text that the component would have to re-parse instead of consuming `MultiChatDispatcher`'s `IAsyncEnumerable<QuadrantEvent>` directly).

### Decision: Model picker options come from `IModelAccessService.GetAvailableModelsAsync` — no separate assignment validation needed

**Decision**: `CompareSessionState`'s per-pane model dropdown is populated from the same `IModelAccessService.GetAvailableModelsAsync(caller)` call Stories 1–2 already use to resolve a default model. `AssignModelAsync` is called only with an `Id` drawn from that list.

**Rationale**: Spec 006 FR-011 already states that "which models a caller may assign to a quadrant is constrained by the role-based model allow-list... this spec does not duplicate that constraint" — and `CosmosMultiChatSessionStore.AssignModelAsync` itself performs no model validation (it only validates the assignment via `MultiChatEndpoints.cs`'s HTTP path, using `IModelCatalogService.GetAsync`, which this UI bypasses). Sourcing the dropdown's options from the caller's own allow-list makes an invalid/disallowed `ModelId` architecturally unreachable from the UI, so no redundant validation call is needed — consistent with Constitution Principle V (the enforcement point is the allow-list computation itself, not a UI-side re-check).

**Alternatives considered**: Calling `IModelCatalogService.GetAsync` after selection to validate (rejected — redundant given the dropdown can only ever offer allowed models; would be dead code on the success path with no corresponding UI path that could trigger its failure branch).

### Decision: `ChatShell.razor` is split into a new `AppShell.razor` (pure layout) + a thin chat-specific wrapper

**Decision**: Extract `ChatShell.razor`'s sidebar-hosting markup (`<ConversationList />` + main-content slot) into a new `AppShell.razor` that takes `ChildContent` (`RenderFragment`) and renders a new `SidebarNav.razor` (static "Compare"/"Changelog" `NavLink`s) above `<ConversationList />`. `ChatShell.razor` keeps its `ThreadId`-driven `SwitchToAsync`/`ResetToNew` logic unchanged, now rendering `<AppShell><!-- chat content --></AppShell>` internally. `Compare.razor` and `Changelog.razor` use `<AppShell>` directly, with no `ChatComposerState` dependency at all.

**Rationale**: Without this split, `/compare` and `/changelog` would each need to either duplicate `<ConversationList />`-hosting markup (violating Constitution Principle IV — a second/third implementation of the same persistent-shell concern) or be forced through `ChatShell`'s `ThreadId`-nullable code path, which would spuriously invoke `ComposerState.ResetToNew()` on every render of an unrelated page. Splitting the pure-layout concern out is the smaller, single-responsibility change, and `ChatShell`'s existing Story 3 tests are unaffected since its own behavior doesn't change, only what it delegates to.

**Alternatives considered**: Adding a `bool ManagesThreadState` flag parameter to `ChatShell` (rejected — a flag to suppress a component's own core behavior is a code smell next to a straightforward extract-component refactor); duplicating the sidebar markup in `Compare.razor`/`Changelog.razor` (rejected — Principle IV).

### Decision: `UpdateBanner` lives in `MainLayout.razor`, not `AppShell.razor`

**Decision**: `UpdateBannerState`/`UpdateBanner.razor` are wired into `MainLayout.razor` (which already wraps every page via `@Body`), rendered above `@Body` alongside the existing conditional dev-auth-banner — not inside `AppShell`.

**Rationale**: Per the 2026-08-28 clarification, the notice must be a global banner "visible on every authenticated page" — `MainLayout.razor` is the only component that already wraps literally every route (including any future non-chat route), whereas `AppShell` is scoped to the four chat/compare/changelog routes that choose to use it. Mirrors the existing dev-auth-banner's placement and conditional-render style exactly, so there's one established pattern for "top-of-page conditional banner," not two.

**Alternatives considered**: Rendering the banner from `AppShell` (rejected — narrower reach than "every authenticated page," and would need duplicating into any future non-`AppShell` route); a toast/snackbar instead of a persistent banner (rejected — not requested by the spec, which calls for a notice that "does not reappear... within the acknowledgment window," implying persistence across the session until dismissed, not an auto-expiring toast).

### Decision: Pane history reloads on page load via the existing `ListByThreadAsync` (Story 3 capability), same as conversation switching

**Decision**: `CompareSessionState.InitializeAsync()` — beyond loading quadrant/model-assignment state via `GetOrCreateAsync` — also loads each quadrant's prior message history via `IChatMessageStore.ListByThreadAsync(quadrant.ThreadId, ...)` where `ThreadId` is already set, populating that pane's transcript.

**Rationale**: Spec 024 AC1 (Story 4) only explicitly requires model *assignment* to persist across reload, but leaving an assigned pane's prior conversation invisible after reload (while its model assignment silently persists) would be a confusing, worse-than-nothing UX with no story or FR blocking a fix — and the capability already exists (added in Story 3, reused here at zero new cost). This mirrors the existing `ChatComposerState.SwitchToAsync` pattern exactly.

**Alternatives considered**: Leaving pane transcripts empty on reload, relying only on quadrant/model state (rejected — inconsistent with Story 3's precedent that reload restores visible conversation state, and gratuitously worse UX for a capability that's already free to reuse).

### Decision: Shared single composer, dispatch fan-out and per-pane event routing handled by the existing merged-channel design

**Decision**: `CompareBoard.razor` has one shared composer bound to `CompareSessionState.ComposerText`. `SendToAllAsync` calls `MultiChatDispatcher.DispatchAsync(caller, session, text, ct)` once and `await foreach`s the returned `IAsyncEnumerable<QuadrantEvent>`, routing each event to `Panes[event.Position]` by `Kind` (`Chunk` → append to that pane's in-progress message; `Error` → set that pane's `ErrorMessage`, mark its message complete; `Done` → mark its message complete) and calling `StateHasChanged()` per event.

**Rationale**: `MultiChatDispatcher` (spec 006) already fans out one `IChatPipeline.SendMessageAsync` call per *assigned* quadrant concurrently and merges results into one channel — a slow or failed quadrant's events simply arrive later/differently on that same channel without blocking others, so FR-009/AC3/AC4's independence requirements fall out of the existing dispatcher design with no new concurrency code in the UI layer. Quadrants with no assigned model are never dispatched to at all (dispatcher only sends to assigned quadrants), so `ComparePane.razor` separately renders a "nothing to send to" indicator purely from `Panes[i].ModelId is null` at render time — a local, dispatcher-independent check (Edge Cases).

**Alternatives considered**: One `IChatPipeline.SendMessageAsync` call per pane issued directly from `CompareSessionState` (rejected — would duplicate `MultiChatDispatcher`'s existing fan-out/merge/thread-creation-on-demand logic in a second place, violating Constitution Principle IV).
