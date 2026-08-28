# Implementation Plan: Chat Web UI — Stories 1–5

**Branch**: `024-chat-web-ui` | **Date**: 2026-08-24 (Stories 1–2) / 2026-08-26 (Story 3 addendum) / 2026-08-28 (Stories 4–5 addendum) | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/024-chat-web-ui/spec.md`

**Scope note**: This plan originally covered only **User Story 1** and **User Story 2** (the P1 stories), then **User Story 3** (conversation list, switch, rename) as a follow-on pass. It now also covers **User Story 4** (multi-pane model comparison) and **User Story 5** (changelog + version-update banner) as a second follow-on pass — the addendum sections below are marked "(Stories 4–5)". All five user stories in spec.md are now covered by this plan.

## Summary

The backend for authenticated chat (specs 002, 014, 004, 006, 017) is fully implemented and merged, but the app's landing page (`Home.razor`) is still scaffold placeholder text — there is no screen a user can actually open to try it. This slice replaces that placeholder with a real chat home screen: an authenticated user lands on a ready-to-type composer, sends a message, and watches the assistant's reply stream in token-by-token, with follow-up messages continuing the same conversation.

Technical approach: because the app already runs Blazor Web App in **Interactive Server** mode, the new component composes the *existing* Application-layer services (`IChatThreadStore`, `IChatPipeline`, `IModelAccessService`, `ICurrentUserAccessor`) directly via DI, rather than adding a new HTTP contract — the component `await foreach`s the pipeline's `IAsyncEnumerable<string>` chunks straight into UI state over the existing SignalR circuit. No new backend capability is introduced.

**(Story 3 addendum)**: Story 3 *does* introduce new backend capability — "list a user's conversations" and "rename a conversation" don't exist anywhere in the codebase today (confirmed by direct code search: `IChatThreadStore` has only `GetAsync`/`CreateAsync`; `IChatMessageStore` has only `AppendAsync`). This adds `ListByOwnerAsync`/`RenameAsync`/`TouchLastActivityAsync` to `IChatThreadStore` and `ListByThreadAsync` to `IChatMessageStore`, backed by the first cross-document Cosmos query and first update-in-place pattern in this codebase (verified: zero existing uses of `GetItemQueryIterator`/`PatchItemAsync`/`ReplaceItemAsync` anywhere in `src/`). The update idiom reuses `CosmosMultiChatSessionStore.SaveAsync`'s existing `UpsertItemAsync` read-modify-write pattern rather than introducing a second update mechanism. The Blazor UI continues calling these stores directly (same rationale as Stories 1–2), but the capability is *also* exposed via new `ChatEndpoints.cs` routes for architectural consistency with 004/006's endpoint-exposed capabilities and for `WebApplicationFactory` testability.

**(Stories 4–5 addendum)**: Unlike Story 3, Stories 4–5 introduce **zero new backend capability** — both backends are already fully implemented and merged: spec 006's `IMultiChatSessionStore` (quadrant CRUD, 2–4 floor/cap already enforced by `MultiChatQuadrantRules`) and `MultiChatDispatcher` (parallel per-quadrant send via the existing `IChatPipeline`, merged into one `IAsyncEnumerable<QuadrantEvent>`) already exist and are already endpoint-exposed (`MultiChatEndpoints.cs`); spec 017's `IChangelogReader`, `IVersionAcknowledgmentStore`, and `AlertWindowEvaluator` likewise already exist and are already endpoint-exposed (`SupportEndpoints.cs`). Confirmed by direct code inspection: `CosmosMultiChatSessionStore.GetOrCreateAsync` already seeds a brand-new session with exactly 2 unassigned quadrants (`Position = 0` and `Position = 1`), which is *why* the 2026-08-28 clarification's "2 unassigned panes by default" answer requires no backend change — it's already the store's behavior. This addendum is purely a Blazor-UI wiring pass: two new routes/pages (`/compare`, `/changelog`), one new persistent-shell nav surface, and Blazor Server components/state classes that call these existing Application-layer services directly via DI (same "component calls services directly, not the HTTP endpoint" rationale established for Stories 1–3).

Building both routes surfaced one structural gap in the existing shell: `ChatShell.razor` (built in Story 3) hard-codes chat-specific behavior (`ThreadId`-driven `SwitchToAsync`/`ResetToNew`) around the sidebar, so `/compare` and `/changelog` can't reuse it as-is without either duplicating the sidebar markup or dragging in irrelevant `ChatComposerState` coupling. This addendum splits it: a new `AppShell.razor` owns *only* the persistent layout (sidebar nav + conversation list + arbitrary child content); `ChatShell.razor` is refactored to be a thin chat-specific wrapper around `AppShell` (unchanged behavior/tests); `Compare.razor` and `Changelog.razor` use `AppShell` directly. This keeps exactly one implementation of the persistent-shell layout (Constitution Principle IV) rather than three copies of the same sidebar markup.

## Technical Context

**Language/Version**: C# / .NET 10 (net10.0), consistent with all existing projects.

**Primary Dependencies**: Existing `EnterpriseAIPlatform.Application`/`.Infrastructure` services only (`IChatThreadStore`, `IChatPipeline`, `IModelAccessService`, `ICurrentUserAccessor`, `IIdentityHasher`). New test-only dependency: `bUnit` (added to `EnterpriseAIPlatform.UnitTests`) for component-render assertions. **(Stories 4–5 addendum)**: also `IMultiChatSessionStore`, `MultiChatDispatcher` (concrete class, no interface — see research.md), `IChangelogReader`, `IVersionAcknowledgmentStore`, `AlertWindowEvaluator` (spec 006/017, all pre-existing, unmodified).

**Storage**: N/A for this slice — no new persisted entities (see [data-model.md](./data-model.md)); all persistence flows through spec 004's existing `ChatThreadModel`/`ChatMessageModel` stores. **(Stories 4–5 addendum)**: likewise no new persisted entities — flows through spec 006's `MultiChatSession` Cosmos doc and spec 017's `VersionAcknowledgmentModel` Cosmos doc / file-system-backed `ChangelogEntry` reads, all pre-existing.

**Testing**: xUnit + NSubstitute (existing convention) for `ChatComposerState` logic in `EnterpriseAIPlatform.UnitTests`; `WebApplicationFactory` (existing convention) in `EnterpriseAIPlatform.IntegrationTests` for the unauthenticated-redirect behavior; new `bUnit` component tests in `EnterpriseAIPlatform.UnitTests` for render behavior (composer disabled while streaming, message list rendering, interrupted-state styling). **(Stories 4–5 addendum)**: same conventions extended to `CompareSessionState`/`UpdateBannerState`; integration tests reuse the existing `FakeMultiChatSessionStore.cs` and `FakeSupportStores.cs` (already in `EnterpriseAIPlatform.IntegrationTests` from specs 006/017) rather than introducing new fakes.

**Target Platform**: Same as existing app — ASP.NET Core / Blazor Web App, Interactive Server render mode, Azure-hosted.

**Project Type**: Web application (existing single `src/EnterpriseAIPlatform.Web` presentation project on top of the existing layered solution — not a new project).

**Performance Goals**: SC-002 — first token visible in under 3 seconds under normal conditions (inherited from spec 004's existing pipeline performance; this slice adds no additional latency beyond DI calls and component re-render). **(Stories 4–5 addendum)**: SC-005's independent-per-pane rendering is inherited from `MultiChatDispatcher`'s existing merged-channel design (spec 006) — this slice adds no new concurrency behavior, only routes its events to per-pane UI state.

**Constraints**: Reuse existing Application-layer contracts only — no new `/api/*` endpoints for this slice (see research.md's decisions). Send action must be disabled while a response is streaming (per Clarifications). **(Stories 4–5 addendum)**: same constraint — no new `/api/*` endpoints; the comparison view's model picker options MUST come from `IModelAccessService.GetAvailableModelsAsync` (the same allow-list spec 006 FR-011 already designates), so an invalid/disallowed `ModelId` is architecturally unreachable from the UI rather than needing a separate validation call.

**Scale/Scope**: Single-user, single active conversation per browser circuit for Stories 1–2. **(Story 3 addendum)**: a user's conversation list and per-conversation message history are both expected to be small (internal pilot scale, per the spec's existing "limited production pilot, employees-only" assumption) — this justifies fetching a user's full conversation list and a full thread's message history in one unpaginated query each, and sorting client-side rather than depending on an unverified Cosmos composite index for `ORDER BY`. **(Stories 4–5 addendum)**: comparison sessions are capped at 4 panes (pre-existing spec 006 invariant); changelog entries are file-system-sourced and expected to be few — no pagination needed for either.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — no changes to this assessment resulted from Phase 1 (Stories 1–2), the Story 3 addendum, or the Stories 4–5 addendum.*

- **I. Azure-Only**: Pass — no new infrastructure/dependencies of any kind (Azure or otherwise) beyond the `bUnit` test package.
- **II. Explicit, Server-Side Authorization**: Pass — caller identity is resolved server-side via `ICurrentUserAccessor.GetCurrentUser()` inside the component (Interactive Server runs server-side), the same accessor `ChatEndpoints.cs` already uses. Unauthenticated routing is enforced by the existing global fallback authorization policy in `Program.cs` (verified directly), not by any new client-side check. The "disable send while streaming" behavior is advisory UX only, consistent with the constitution's framing — no new security boundary is claimed for it.
- **III. Fail Loud, Never Fabricate Success**: Pass — unexpected pipeline failures surface a generic, non-alarming error (mirroring `ChatEndpoints.cs`'s catch-all 500 handling); interrupted streams are visibly marked interrupted (FR-012), never silently presented as complete.
- **IV. One Implementation Per Concern**: Pass — the component calls the *same* `IChatThreadStore`/`IChatPipeline`/`IModelAccessService` instances the existing endpoints call; no business logic (thread creation, message sending, model entitlement) is reimplemented. The component's `ChatSendResult` switch mirrors `ChatEndpoints.MapRejection`'s cases, but as a rendering concern (user-facing text vs. HTTP status) parallel to how the same result already renders differently for JSON vs. SSE — not a second implementation of the underlying decision logic.
- **V. Schema-Enforced Validation**: Pass — message-length/rate-limit/content-policy enforcement remains entirely server-side in the existing pipeline; the UI only surfaces results, adding no client-only validation that could diverge from server behavior.
- **VI. Testable, EARS-Style Requirements**: Pass — spec 024's FRs (as clarified) are already in this form; this plan's test list (research.md) gives each in-scope FR (FR-001–FR-004, FR-012) an independent test.

**(Story 3 addendum)**:
- **II. Explicit, Server-Side Authorization**: Pass — every new store method is partition-key-scoped to the caller's own hashed identity (`QueryRequestOptions.PartitionKey` on the list queries; the existing ownership-scoped `GetAsync` point-read before any rename/history-load). A foreign or nonexistent thread ID produces the same `NotFound` outcome from the same code path — there is no separate "is this mine?" branch that could be forgotten (this is exactly the class of defect Principle II's rationale cites).
- **IV. One Implementation Per Concern**: Pass — `RenameAsync`/`TouchLastActivityAsync` reuse `CosmosMultiChatSessionStore.SaveAsync`'s existing `UpsertItemAsync` idiom rather than introducing `PatchItemAsync` as a second update mechanism for one call site.
- **V. Schema-Enforced Validation**: Pass — the empty/whitespace rename rejection lives in a new Application-layer `ConversationRenameRules` validator called from the store, not only from the Blazor component — a direct API caller hitting `PATCH /api/chat/threads/{id}` gets the same enforcement.

**(Stories 4–5 addendum)**:
- **II. Explicit, Server-Side Authorization**: Pass — `CompareSessionState`/`UpdateBannerState` resolve the caller server-side via the same `ICurrentUserAccessor` used everywhere else in this plan; every `IMultiChatSessionStore`/`IVersionAcknowledgmentStore` call is partition-key-scoped to the caller's own hashed identity, identical to the existing `MultiChatEndpoints.cs`/`SupportEndpoints.cs` callers of these same stores.
- **IV. One Implementation Per Concern**: Pass — no new business logic is introduced; the UI calls the *same* `IMultiChatSessionStore`/`MultiChatDispatcher`/`IChangelogReader`/`IVersionAcknowledgmentStore`/`AlertWindowEvaluator` instances the existing endpoints call. The `ChatShell`→`AppShell` split (see Summary) exists specifically to keep the persistent-shell layout as one implementation rather than three copies across `/`, `/compare`, and `/changelog`.
- **III. Fail Loud, Never Fabricate Success**: Pass — `UpdateBannerState.DismissAsync`'s optimistic dismiss is reverted if `IVersionAcknowledgmentStore.SetAsync` throws (mirroring `SupportEndpoints.cs`'s own comment that a failed persist "must never look like success"); a pane's send failure surfaces as that pane's `ErrorMessage`, never a silently-dropped event.
- **V. Schema-Enforced Validation**: Pass — no new validation is introduced by this UI; quadrant-count and model-assignment rules remain enforced entirely in spec 006's existing `MultiChatQuadrantRules`/store.

No violations requiring Complexity Tracking justification (Stories 1–2, Story 3, or Stories 4–5).

## Project Structure

### Documentation (this feature)

```text
specs/024-chat-web-ui/
├── plan.md              # This file
├── research.md          # Phase 0 output (Stories 1–2 + Story 3 + Stories 4–5 addenda)
├── data-model.md         # Phase 1 output (Stories 1–2 + Story 3 + Stories 4–5 addenda)
├── quickstart.md         # Phase 1 output (Stories 1–2 + Story 3 + Stories 4–5 addenda)
├── contracts/
│   ├── ui-integration-contract.md   # Phase 1 output (Stories 1–2 + Story 3 + Stories 4–5 addenda) — no new HTTP contract file for Stories 4–5 (no new endpoints; see Summary)
│   └── chat-threads-http-contract.md # NEW (Story 3) — the 3 new HTTP endpoints
└── tasks.md              # Phase 2 output (/speckit-tasks — not created by this command)
```

### Source Code (repository root)

```text
src/EnterpriseAIPlatform.Domain/Chat/
└── ChatThreadModel.cs                  # MODIFIED (Story 3): +DisplayName, +LastActivityAtUtc

src/EnterpriseAIPlatform.Application/Chat/
├── IChatThreadStore.cs                 # MODIFIED (Story 3): +ListByOwnerAsync, +RenameAsync, +TouchLastActivityAsync
├── IChatMessageStore.cs                # MODIFIED (Story 3): +ListByThreadAsync
└── ConversationRenameRules.cs          # NEW (Story 3): shared non-empty/whitespace validator

src/EnterpriseAIPlatform.Infrastructure/Chat/
├── ChatCosmosDocuments.cs              # MODIFIED (Story 3): ChatThreadDocument +DisplayName/+LastActivityAtUtc
├── CosmosChatThreadStore.cs            # MODIFIED (Story 3): new methods per Application layer
├── CosmosChatMessageStore.cs           # MODIFIED (Story 3): ListByThreadAsync
└── ChatPipeline.cs                     # MODIFIED (Story 3): call TouchLastActivityAsync after persistence

src/EnterpriseAIPlatform.Web/
├── Endpoints/Chat/
│   └── ChatEndpoints.cs                # MODIFIED (Story 3): +GET /api/chat/threads, +GET .../{id}/messages, +PATCH .../{id}
├── Components/
│   ├── Pages/
│   │   ├── Home.razor                 # MODIFIED (US1/US2), MODIFIED again (Story 3): hosts ChatShell instead of ChatHome directly
│   │   ├── ChatConversation.razor     # NEW (Story 3): thin @page "/chat/{ThreadId}" host
│   │   ├── Compare.razor              # NEW (Story 4): thin @page "/compare" host — hosts <AppShell><CompareBoard /></AppShell>
│   │   └── Changelog.razor            # NEW (Story 5): thin @page "/changelog" host — hosts <AppShell><ChangelogView /></AppShell>
│   ├── Chat/                          # presentation sub-components
│   │   ├── ChatComposer.razor         # US1/US2
│   │   ├── ChatTranscript.razor       # US1/US2; REUSED as-is (Story 4) to render each comparison pane's transcript
│   │   ├── ChatShell.razor            # NEW (Story 3); MODIFIED (Stories 4–5): refactored to a thin chat-specific wrapper around new AppShell
│   │   ├── AppShell.razor             # NEW (Stories 4–5): persistent layout — SidebarNav + ConversationList + ChildContent (extracted from ChatShell)
│   │   ├── SidebarNav.razor           # NEW (Stories 4–5): "Compare"/"Changelog" NavLinks, peer to ConversationList inside AppShell
│   │   ├── ConversationList.razor     # NEW (Story 3): sidebar list, inline-edit rename
│   │   ├── CompareBoard.razor         # NEW (Story 4): shared composer/send bar + pane grid, add/remove-pane controls
│   │   └── ComparePane.razor          # NEW (Story 4): one quadrant — model picker + reused ChatTranscript + per-pane error state
│   ├── Support/
│   │   ├── ChangelogView.razor        # NEW (Story 5): newest-first entry list + empty state
│   │   └── UpdateBanner.razor         # NEW (Story 5): dismissible global banner
│   └── Layout/
│       └── MainLayout.razor           # MODIFIED (Story 5): hosts <UpdateBanner /> above @Body, alongside the existing dev-auth-banner
└── Services/
    ├── ChatComposerState.cs           # US1/US2; MODIFIED (Story 3): +SwitchToAsync, +ResetToNewAsync, +IsNotFound, +IChatMessageStore dependency
    ├── ConversationListState.cs       # NEW (Story 3): Scoped, owns the list + rename flow
    ├── CompareSessionState.cs         # NEW (Story 4): Scoped, owns quadrant state + dispatch via MultiChatDispatcher
    └── UpdateBannerState.cs           # NEW (Story 5): Scoped, owns ShowAlert/dismiss flow

tests/
├── EnterpriseAIPlatform.UnitTests/
│   ├── Web/
│   │   ├── ChatComposerStateTests.cs   # US1/US2; extended (Story 3): SwitchToAsync/not-found cases
│   │   ├── ChatComposerComponentTests.cs  # US1/US2
│   │   ├── ConversationListStateTests.cs  # NEW (Story 3)
│   │   ├── ConversationListComponentTests.cs # NEW (Story 3): bUnit — empty state, inline rename, not-found
│   │   ├── CompareSessionStateTests.cs    # NEW (Story 4): add/remove-pane cap/floor, model-assign, dispatch event routing
│   │   ├── ComparePaneComponentTests.cs   # NEW (Story 4): bUnit — no-model-assigned indicator, per-pane error, independent streaming render
│   │   ├── UpdateBannerStateTests.cs      # NEW (Story 5): show/dismiss, revert-on-persist-failure
│   │   └── ChangelogViewComponentTests.cs # NEW (Story 5): bUnit — newest-first render, empty state
│   └── EnterpriseAIPlatform.UnitTests.csproj
├── EnterpriseAIPlatform.IntegrationTests/
│   ├── Web/
│   │   └── ChatHomeAuthTests.cs        # US1/US2
│   ├── FakeChatStores.cs               # MODIFIED (Story 3): +ListByOwnerAsync, +RenameAsync, +TouchLastActivityAsync, +ListByThreadAsync
│   ├── FakeMultiChatSessionStore.cs    # UNCHANGED (already exists from spec 006) — reused directly (Story 4)
│   ├── FakeSupportStores.cs            # UNCHANGED (already exists from spec 017) — reused directly (Story 5)
│   └── ChatConversationListTests.cs    # NEW (Story 3): list-scoped-to-owner, rename (persist/empty-rejected/wrong-owner-404), message-history (seeded/wrong-owner-404)
└── EnterpriseAIPlatform.ArchitectureTests/  # UNCHANGED — new methods land on the existing single implementations
```

**Structure Decision**: Extend the existing single-solution layout — no new projects. `ChatComposerState` and (Story 3) `ConversationListState` are both registered `Scoped` in DI. Component markup stays thin; all branching logic lives in the state classes so it's unit-testable without bUnit, with bUnit reserved for render-level assertions. **(Story 3)**: `ConversationListState` is a sibling of `ChatComposerState`, not a merge into it — it owns the list/rename flow and calls `ChatComposerState.SwitchToAsync` on selection, keeping a single owner of "what thread is active" rather than splitting that across two classes. **(Stories 4–5)**: `CompareSessionState` and `UpdateBannerState` are likewise new `Scoped` siblings, each owning one concern (comparison-session state; banner state) with no cross-dependencies on `ChatComposerState`/`ConversationListState`. `AppShell.razor` is extracted from `ChatShell.razor` so the persistent sidebar (nav links + conversation list) has exactly one implementation shared by all four routes (`/`, `/chat/{id}`, `/compare`, `/changelog`) instead of being duplicated.

## Complexity Tracking

*No entries — no Constitution Check violations.*
