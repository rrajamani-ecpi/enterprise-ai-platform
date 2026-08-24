# Implementation Plan: Chat Web UI — Stories 1–2 slice

**Branch**: `024-chat-web-ui` | **Date**: 2026-08-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/024-chat-web-ui/spec.md`

**Scope note**: This plan covers only **User Story 1** (sign-in/chat home landing) and **User Story 2** (start a conversation, watch it stream, send follow-ups) — the P1 stories, chosen so there is a real, usable chat screen to test against as soon as possible. **User Stories 3–5** (conversation list/rename, multi-pane comparison, changelog/version-alert) are explicitly deferred to a later `/speckit-plan` pass, per the user's direction and consistent with the spec's own priority ordering (Story 2 alone is a viable MVP).

## Summary

The backend for authenticated chat (specs 002, 014, 004, 006, 017) is fully implemented and merged, but the app's landing page (`Home.razor`) is still scaffold placeholder text — there is no screen a user can actually open to try it. This slice replaces that placeholder with a real chat home screen: an authenticated user lands on a ready-to-type composer, sends a message, and watches the assistant's reply stream in token-by-token, with follow-up messages continuing the same conversation.

Technical approach: because the app already runs Blazor Web App in **Interactive Server** mode, the new component composes the *existing* Application-layer services (`IChatThreadStore`, `IChatPipeline`, `IModelAccessService`, `ICurrentUserAccessor`) directly via DI, rather than adding a new HTTP contract — the component `await foreach`s the pipeline's `IAsyncEnumerable<string>` chunks straight into UI state over the existing SignalR circuit. No new backend capability is introduced.

## Technical Context

**Language/Version**: C# / .NET 10 (net10.0), consistent with all existing projects.

**Primary Dependencies**: Existing `EnterpriseAIPlatform.Application`/`.Infrastructure` services only (`IChatThreadStore`, `IChatPipeline`, `IModelAccessService`, `ICurrentUserAccessor`, `IIdentityHasher`). New test-only dependency: `bUnit` (added to `EnterpriseAIPlatform.UnitTests`) for component-render assertions.

**Storage**: N/A for this slice — no new persisted entities (see [data-model.md](./data-model.md)); all persistence flows through spec 004's existing `ChatThreadModel`/`ChatMessageModel` stores.

**Testing**: xUnit + NSubstitute (existing convention) for `ChatComposerState` logic in `EnterpriseAIPlatform.UnitTests`; `WebApplicationFactory` (existing convention) in `EnterpriseAIPlatform.IntegrationTests` for the unauthenticated-redirect behavior; new `bUnit` component tests in `EnterpriseAIPlatform.UnitTests` for render behavior (composer disabled while streaming, message list rendering, interrupted-state styling).

**Target Platform**: Same as existing app — ASP.NET Core / Blazor Web App, Interactive Server render mode, Azure-hosted.

**Project Type**: Web application (existing single `src/EnterpriseAIPlatform.Web` presentation project on top of the existing layered solution — not a new project).

**Performance Goals**: SC-002 — first token visible in under 3 seconds under normal conditions (inherited from spec 004's existing pipeline performance; this slice adds no additional latency beyond DI calls and component re-render).

**Constraints**: Reuse existing Application-layer contracts only — no new `/api/*` endpoints for this slice (see research.md's decisions). Send action must be disabled while a response is streaming (per Clarifications).

**Scale/Scope**: Single-user, single active conversation per browser circuit for this slice (no list/switch — Story 3). Employees-only desktop pilot, consistent with existing scope.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — no changes to this assessment resulted from Phase 1.*

- **I. Azure-Only**: Pass — no new infrastructure/dependencies of any kind (Azure or otherwise) beyond the `bUnit` test package.
- **II. Explicit, Server-Side Authorization**: Pass — caller identity is resolved server-side via `ICurrentUserAccessor.GetCurrentUser()` inside the component (Interactive Server runs server-side), the same accessor `ChatEndpoints.cs` already uses. Unauthenticated routing is enforced by the existing global fallback authorization policy in `Program.cs` (verified directly), not by any new client-side check. The "disable send while streaming" behavior is advisory UX only, consistent with the constitution's framing — no new security boundary is claimed for it.
- **III. Fail Loud, Never Fabricate Success**: Pass — unexpected pipeline failures surface a generic, non-alarming error (mirroring `ChatEndpoints.cs`'s catch-all 500 handling); interrupted streams are visibly marked interrupted (FR-012), never silently presented as complete.
- **IV. One Implementation Per Concern**: Pass — the component calls the *same* `IChatThreadStore`/`IChatPipeline`/`IModelAccessService` instances the existing endpoints call; no business logic (thread creation, message sending, model entitlement) is reimplemented. The component's `ChatSendResult` switch mirrors `ChatEndpoints.MapRejection`'s cases, but as a rendering concern (user-facing text vs. HTTP status) parallel to how the same result already renders differently for JSON vs. SSE — not a second implementation of the underlying decision logic.
- **V. Schema-Enforced Validation**: Pass — message-length/rate-limit/content-policy enforcement remains entirely server-side in the existing pipeline; the UI only surfaces results, adding no client-only validation that could diverge from server behavior.
- **VI. Testable, EARS-Style Requirements**: Pass — spec 024's FRs (as clarified) are already in this form; this plan's test list (research.md) gives each in-scope FR (FR-001–FR-004, FR-012) an independent test.

No violations requiring Complexity Tracking justification.

## Project Structure

### Documentation (this feature)

```text
specs/024-chat-web-ui/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output
├── quickstart.md         # Phase 1 output
├── contracts/
│   └── ui-integration-contract.md   # Phase 1 output
└── tasks.md              # Phase 2 output (/speckit-tasks — not created by this command)
```

### Source Code (repository root)

```text
src/EnterpriseAIPlatform.Web/
├── Components/
│   ├── Pages/
│   │   └── Home.razor                 # MODIFIED: replaces placeholder with chat home (US1) + composer/transcript (US2)
│   ├── Chat/                          # NEW: presentation sub-components for the composer/transcript
│   │   ├── ChatComposer.razor
│   │   └── ChatTranscript.razor
│   └── Layout/
│       └── MainLayout.razor           # UNCHANGED for this pass
└── Services/
    └── ChatComposerState.cs           # NEW: per-circuit (Scoped) state/coordination class — see data-model.md

tests/
├── EnterpriseAIPlatform.UnitTests/
│   ├── Web/
│   │   ├── ChatComposerStateTests.cs   # NEW: NSubstitute over IChatPipeline/IChatThreadStore/IModelAccessService/ICurrentUserAccessor
│   │   └── ChatComposerComponentTests.cs  # NEW: bUnit — disabled-while-streaming, interrupted rendering
│   └── EnterpriseAIPlatform.UnitTests.csproj  # MODIFIED: add bUnit package reference
└── EnterpriseAIPlatform.IntegrationTests/
    └── Web/
        └── ChatHomeAuthTests.cs        # NEW: WebApplicationFactory — unauthenticated request to "/" is challenged, not rendered
```

**Structure Decision**: Extend the existing single-solution layout — no new projects. `ChatComposerState` is registered `Scoped` in DI (per-circuit lifetime matches Blazor Server's per-connection scope) alongside the other `Program.cs` service registrations. Component markup stays thin (binds to `ChatComposerState`); all branching logic (model resolution, `ChatSendResult` handling, interrupted-stream detection) lives in `ChatComposerState` so it's unit-testable without bUnit, with bUnit reserved for the render-level assertions NSubstitute can't cover.

## Complexity Tracking

*No entries — no Constitution Check violations.*
