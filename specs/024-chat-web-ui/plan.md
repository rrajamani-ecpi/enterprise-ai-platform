# Implementation Plan: Chat Web UI — Stories 1–3

**Branch**: `024-chat-web-ui` | **Date**: 2026-08-24 (Stories 1–2) / 2026-08-26 (Story 3 addendum) | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/024-chat-web-ui/spec.md`

**Scope note**: This plan originally covered only **User Story 1** and **User Story 2** (the P1 stories). It now also covers **User Story 3** (conversation list, switch, rename), added as a follow-on pass once Stories 1–2 were implemented, tested, and confirmed working live — the addendum sections below are marked "(Story 3)". **User Stories 4–5** (multi-pane comparison, changelog/version-alert) remain deferred to a later `/speckit-plan` pass.

## Summary

The backend for authenticated chat (specs 002, 014, 004, 006, 017) is fully implemented and merged, but the app's landing page (`Home.razor`) is still scaffold placeholder text — there is no screen a user can actually open to try it. This slice replaces that placeholder with a real chat home screen: an authenticated user lands on a ready-to-type composer, sends a message, and watches the assistant's reply stream in token-by-token, with follow-up messages continuing the same conversation.

Technical approach: because the app already runs Blazor Web App in **Interactive Server** mode, the new component composes the *existing* Application-layer services (`IChatThreadStore`, `IChatPipeline`, `IModelAccessService`, `ICurrentUserAccessor`) directly via DI, rather than adding a new HTTP contract — the component `await foreach`s the pipeline's `IAsyncEnumerable<string>` chunks straight into UI state over the existing SignalR circuit. No new backend capability is introduced.

**(Story 3 addendum)**: Story 3 *does* introduce new backend capability — "list a user's conversations" and "rename a conversation" don't exist anywhere in the codebase today (confirmed by direct code search: `IChatThreadStore` has only `GetAsync`/`CreateAsync`; `IChatMessageStore` has only `AppendAsync`). This adds `ListByOwnerAsync`/`RenameAsync`/`TouchLastActivityAsync` to `IChatThreadStore` and `ListByThreadAsync` to `IChatMessageStore`, backed by the first cross-document Cosmos query and first update-in-place pattern in this codebase (verified: zero existing uses of `GetItemQueryIterator`/`PatchItemAsync`/`ReplaceItemAsync` anywhere in `src/`). The update idiom reuses `CosmosMultiChatSessionStore.SaveAsync`'s existing `UpsertItemAsync` read-modify-write pattern rather than introducing a second update mechanism. The Blazor UI continues calling these stores directly (same rationale as Stories 1–2), but the capability is *also* exposed via new `ChatEndpoints.cs` routes for architectural consistency with 004/006's endpoint-exposed capabilities and for `WebApplicationFactory` testability.

## Technical Context

**Language/Version**: C# / .NET 10 (net10.0), consistent with all existing projects.

**Primary Dependencies**: Existing `EnterpriseAIPlatform.Application`/`.Infrastructure` services only (`IChatThreadStore`, `IChatPipeline`, `IModelAccessService`, `ICurrentUserAccessor`, `IIdentityHasher`). New test-only dependency: `bUnit` (added to `EnterpriseAIPlatform.UnitTests`) for component-render assertions.

**Storage**: N/A for this slice — no new persisted entities (see [data-model.md](./data-model.md)); all persistence flows through spec 004's existing `ChatThreadModel`/`ChatMessageModel` stores.

**Testing**: xUnit + NSubstitute (existing convention) for `ChatComposerState` logic in `EnterpriseAIPlatform.UnitTests`; `WebApplicationFactory` (existing convention) in `EnterpriseAIPlatform.IntegrationTests` for the unauthenticated-redirect behavior; new `bUnit` component tests in `EnterpriseAIPlatform.UnitTests` for render behavior (composer disabled while streaming, message list rendering, interrupted-state styling).

**Target Platform**: Same as existing app — ASP.NET Core / Blazor Web App, Interactive Server render mode, Azure-hosted.

**Project Type**: Web application (existing single `src/EnterpriseAIPlatform.Web` presentation project on top of the existing layered solution — not a new project).

**Performance Goals**: SC-002 — first token visible in under 3 seconds under normal conditions (inherited from spec 004's existing pipeline performance; this slice adds no additional latency beyond DI calls and component re-render).

**Constraints**: Reuse existing Application-layer contracts only — no new `/api/*` endpoints for this slice (see research.md's decisions). Send action must be disabled while a response is streaming (per Clarifications).

**Scale/Scope**: Single-user, single active conversation per browser circuit for Stories 1–2. **(Story 3 addendum)**: a user's conversation list and per-conversation message history are both expected to be small (internal pilot scale, per the spec's existing "limited production pilot, employees-only" assumption) — this justifies fetching a user's full conversation list and a full thread's message history in one unpaginated query each, and sorting client-side rather than depending on an unverified Cosmos composite index for `ORDER BY`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — no changes to this assessment resulted from Phase 1 (Stories 1–2) or from the Story 3 addendum's design.*

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

No violations requiring Complexity Tracking justification (Stories 1–2 or Story 3).

## Project Structure

### Documentation (this feature)

```text
specs/024-chat-web-ui/
├── plan.md              # This file
├── research.md          # Phase 0 output (Stories 1–2 + Story 3 addendum)
├── data-model.md         # Phase 1 output (Stories 1–2 + Story 3 addendum)
├── quickstart.md         # Phase 1 output (Stories 1–2 + Story 3 addendum)
├── contracts/
│   ├── ui-integration-contract.md   # Phase 1 output (Stories 1–2 + Story 3 addendum)
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
│   │   └── ChatConversation.razor     # NEW (Story 3): thin @page "/chat/{ThreadId}" host
│   ├── Chat/                          # presentation sub-components
│   │   ├── ChatComposer.razor         # US1/US2
│   │   ├── ChatTranscript.razor       # US1/US2
│   │   ├── ChatShell.razor            # NEW (Story 3): persistent sidebar layout host
│   │   └── ConversationList.razor     # NEW (Story 3): sidebar list, inline-edit rename
│   └── Layout/
│       └── MainLayout.razor           # UNCHANGED
└── Services/
    ├── ChatComposerState.cs           # US1/US2; MODIFIED (Story 3): +SwitchToAsync, +ResetToNewAsync, +IsNotFound, +IChatMessageStore dependency
    └── ConversationListState.cs       # NEW (Story 3): Scoped, owns the list + rename flow

tests/
├── EnterpriseAIPlatform.UnitTests/
│   ├── Web/
│   │   ├── ChatComposerStateTests.cs   # US1/US2; extended (Story 3): SwitchToAsync/not-found cases
│   │   ├── ChatComposerComponentTests.cs  # US1/US2
│   │   ├── ConversationListStateTests.cs  # NEW (Story 3)
│   │   └── ConversationListComponentTests.cs # NEW (Story 3): bUnit — empty state, inline rename, not-found
│   └── EnterpriseAIPlatform.UnitTests.csproj
├── EnterpriseAIPlatform.IntegrationTests/
│   ├── Web/
│   │   └── ChatHomeAuthTests.cs        # US1/US2
│   ├── FakeChatStores.cs               # MODIFIED (Story 3): +ListByOwnerAsync, +RenameAsync, +TouchLastActivityAsync, +ListByThreadAsync
│   └── ChatConversationListTests.cs    # NEW (Story 3): list-scoped-to-owner, rename (persist/empty-rejected/wrong-owner-404), message-history (seeded/wrong-owner-404)
└── EnterpriseAIPlatform.ArchitectureTests/  # UNCHANGED — new methods land on the existing single implementations
```

**Structure Decision**: Extend the existing single-solution layout — no new projects. `ChatComposerState` and (Story 3) `ConversationListState` are both registered `Scoped` in DI. Component markup stays thin; all branching logic lives in the state classes so it's unit-testable without bUnit, with bUnit reserved for render-level assertions. **(Story 3)**: `ConversationListState` is a sibling of `ChatComposerState`, not a merge into it — it owns the list/rename flow and calls `ChatComposerState.SwitchToAsync` on selection, keeping a single owner of "what thread is active" rather than splitting that across two classes.

## Complexity Tracking

*No entries — no Constitution Check violations.*
