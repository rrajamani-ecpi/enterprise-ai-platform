# Feature Specification: Tools/Extensions Framework

**Feature Branch**: `020-tools-extensions-framework`

**Created**: 2026-07-21

**Status**: Draft

**Input**: Derived from `docs/PRODUCT_REQUIREMENTS_DOCUMENT.md` §4.4 (Tools/Extensions) and routed here per `docs/prd-decomposition-plan.md`, which notes that the layered gating model (`requiresAdmin`/`isDemo`/`requiresAdvancedModel`/`allowedModels`) and the tool catalog itself deserve their own spec rather than living inside chat-pipeline. Spec 004 (Chat Message Pipeline) already covers in-flight tool-failure-handling consistency (its User Story 4 / FR-011–FR-012) — that story is cross-referenced, not duplicated, here. This spec owns the cross-cutting gating framework, execution-timeout contract, tool-enablement persistence, and the minimum tool catalog list; the specific behavior of individual tools already owned by other specs (DataProduct query authorization — spec 012/019; CanvasStudentContext — spec 007/008; ImageGen, and other Chat Core tool mechanics — spec 004; A2A agent invocation — spec 011; model allow-list resolution mechanics — spec 014) is cross-referenced rather than re-specified. Constitution Principle II ("Explicit, Server-Side Authorization for Every Access") already establishes that tool availability and model/entitlement selection MUST be resolved server-side, with UI gating treated as advisory only — this spec is the concrete application of that principle to the tool/extension surface.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Tool availability is always resolved server-side through all four gating layers (Priority: P1)

A chat request is about to expose a set of tools to the model. Each registered tool carries gating metadata — `requiresAdmin`, `isDemo`, `requiresAdvancedModel`, `allowedModels` — and the server must evaluate all four layers for the caller and active model before a tool is ever presented to the model as callable, regardless of what the client believes is visible or enabled.

**Why this priority**: This is the authoritative security boundary for the entire tool surface. Per constitution Principle II, "tool availability... MUST all be resolved server-side; any client-side gating... is advisory UX only and is never itself a security boundary." If any one of the four layers can be satisfied by client-supplied state instead of a server-side check, an unauthorized or under-privileged caller can invoke an admin-only or model-tier-restricted tool. Nothing else in this spec matters until this holds for every tool, every request.

**Independent Test**: For a tool marked `requiresAdmin`, call the chat endpoint directly (bypassing the UI) as a non-admin and confirm the tool is never exposed to the model or invocable, even if the client requests it. Separately, for a tool with a per-tool `allowedModels` list, request it while using a model outside that list and confirm it is excluded. Separately, for a tool marked `requiresAdvancedModel`, request it against a model lacking tool/function-calling capability and confirm it is excluded.

**Acceptance Scenarios**:

1. **Given** a tool with `requiresAdmin: true`, **When** a non-admin caller's request would otherwise include it, **Then** the server excludes it from the set of tools exposed to the model, independent of any client-supplied enablement flag.
2. **Given** a tool with a per-tool `allowedModels` allow-list, **When** the active model for the request is not on that list, **Then** the server excludes the tool from the model-facing tool set, even if the client marks it enabled.
3. **Given** a tool with `requiresAdvancedModel: true`, **When** the active model lacks tool/function-calling capability, **Then** the tool is excluded from the model-facing tool set.
4. **Given** a tool with `isDemo: true`, **When** evaluating server-side exposure, **Then** `isDemo` affects only UI visibility/labeling and is never treated as a security boundary — a demo-flagged tool that also passes the other three layers remains genuinely invocable.
5. **Given** all four gating layers pass for a given caller, model, and tool, **When** the request is processed, **Then** the tool is exposed to the model as callable.
6. **Given** a direct API request that bypasses the chat UI entirely, **When** it specifies a tool that fails any one of the `requiresAdmin`/`requiresAdvancedModel`/`allowedModels` checks, **Then** the tool is still excluded — server-side evaluation does not depend on having gone through the UI.

---

### User Story 2 - The platform ships a minimum viable tool catalog (Priority: P1)

A user chats with a persona that has tools enabled and needs the model to be able to query a knowledge collection, query documents attached to the current chat, search the web, do arithmetic, generate an image, export data as CSV, look up a map/route, check weather, or render an interactive artifact — using whichever of these tools are enabled and gated-available for their context.

**Why this priority**: Per PRD REQ-TOOL-5, this is the baseline capability set the tool framework must support; without at least these tools registered and invocable, the chat experience cannot deliver on its documented feature set. Ranked P1 alongside the gating story because a gating framework with no tools to gate is not a shippable feature, and a tool catalog with no gating is a security gap — both are required for a minimum viable release.

**Independent Test**: With a caller/persona/model combination for which every gating layer passes, exercise each catalog tool (knowledge-collection query, per-chat document query, web search/research, calculator, image generation, CSV export, maps, weather, interactive artifacts) in a chat turn and confirm the model can invoke it and receive a result.

**Acceptance Scenarios**:

1. **Given** a persona/model/caller combination that passes all gating layers, **When** the model is asked a question requiring a registered knowledge-collection query tool, **Then** the tool is invocable and returns a result (specific query/authorization semantics owned by spec 012/019, not re-specified here).
2. **Given** the same conditions, **When** the model is asked about a document attached to the current chat, **Then** the per-chat document query tool is invocable (mechanics owned by spec 004/005, not re-specified here).
3. **Given** the same conditions, **When** the model needs current information, **Then** the web search/research tool is invocable.
4. **Given** the same conditions, **When** the model needs to perform arithmetic, **Then** the calculator tool is invocable.
5. **Given** the same conditions, **When** the model is asked to produce an image, **Then** the image-generation tool is invocable (specific behavior owned by spec 004, not re-specified here).
6. **Given** the same conditions, **When** the model is asked to export tabular data, **Then** the CSV export tool is invocable (injection-safety behavior owned by spec 004 FR-023, not re-specified here).
7. **Given** the same conditions, **When** the model is asked for a location or route, **Then** the map/route tool is invocable.
8. **Given** the same conditions, **When** the model is asked for a weather forecast, **Then** the weather tool is invocable.
9. **Given** the same conditions, **When** the model is asked to produce interactive code/UI, **Then** the artifact tool is invocable (isolation/sandboxing behavior owned by spec 004 User Story 2, not re-specified here).
10. **Given** any catalog tool, **When** it completes its turn, **Then** a final-answer control tool is available to the model to signal completion of tool use and transition to a final response.

---

### User Story 3 - A stalled or failing tool call never hangs or crashes the response (Priority: P2)

A tool call takes longer than its configured timeout, or fails outright during execution.

**Why this priority**: Timeouts and failures are inevitable in any system that calls external services (web search, maps, weather, image generation). This spec owns the timeout contract (the ceiling and its enforcement); spec 004's User Story 4 already owns the specific failure-shape consistency question (why Calculator/Weather previously threw raw exceptions while other tools returned `{success:false}`, and the requirement that all tools converge on one shape) and is cross-referenced rather than duplicated here.

**Independent Test**: Configure a tool with a short timeout, force it to exceed that timeout, and confirm the stream continues with a "timed out" result rather than hanging; separately, confirm the timeout ceiling cannot be configured above the platform-wide maximum override.

**Acceptance Scenarios**:

1. **Given** a tool call that exceeds its configured timeout (default per spec 004 FR-009: 30s, overridable up to 150s), **When** the timeout elapses, **Then** the system returns a "timed out" result to the model rather than allowing the call to hang indefinitely.
2. **Given** a tool call that fails (timeout or exception), **When** the failure occurs, **Then** the resulting failure payload shape is the same structured shape spec 004 (FR-011/FR-012) defines for all tools — this spec does not introduce a second failure envelope.
3. **Given** a per-tool timeout override, **When** it is configured, **Then** it cannot exceed the platform-wide maximum (150s per spec 004 FR-009).

---

### User Story 4 - Per-user/per-persona tool enablement is persisted and re-validated every request (Priority: P2)

A user or persona owner turns specific tools on or off for a persona (e.g., enabling web search but not image generation). That enablement choice persists across sessions, and every subsequent request re-validates it — both against the stored enablement state and against the four gating layers from User Story 1 — rather than trusting a client-cached enablement snapshot.

**Why this priority**: Without persistence, tool preferences would not survive a session, degrading usability; without server-side re-validation on every request, a stale or tampered client-side enablement flag could re-enable a tool that was since disabled or that no longer passes gating (e.g., after an admin revokes `requiresAdmin` access, or a persona's assigned model changes). This is ranked below the P1 gating/catalog stories because it is a refinement of when tools are considered "on," not the authorization boundary itself — but it must hold before general availability.

**Independent Test**: Enable a subset of tools for a persona, reload the session, and confirm only that subset is offered; then disable a previously-enabled tool and confirm the very next request excludes it without requiring a new session; separately, revoke a gating condition (e.g., remove admin role) for a caller with an admin-only tool previously enabled and confirm the next request excludes it even though the stored enablement flag is still "on."

**Acceptance Scenarios**:

1. **Given** a user or persona owner enables or disables specific tools, **When** the choice is saved, **Then** it persists across sessions (survives logout/login, page reload).
2. **Given** a persisted per-user/per-persona tool enablement state, **When** a new chat request is made, **Then** the server re-reads the current stored enablement state rather than trusting any client-cached value.
3. **Given** a tool is enabled in stored state but now fails one of the four gating layers (User Story 1) due to a change since it was last checked (e.g., role change, model change, admin flag revoked), **When** the next request is processed, **Then** the tool is excluded — stored enablement is necessary but never sufficient for exposure.
4. **Given** a tool disabled in stored state, **When** a request is processed, **Then** the tool is excluded regardless of whether it would otherwise pass all four gating layers.

---

### User Story 5 - Tools are grouped for discoverability in the UI (Priority: P3)

A user browsing available tools/extensions in a settings or persona-configuration surface sees them organized into groups (e.g., Basic, Web, Microsoft 365) rather than a single flat list.

**Why this priority**: This is a discoverability/UX affordance with no bearing on what is actually invocable. Per constitution Principle II, UI-level organization and visibility are advisory only and are never themselves a security boundary — a tool's group membership must not be read anywhere as an authorization signal. Ranked lowest because it affects usability, not correctness or safety.

**Independent Test**: Render the tool/extension picker and confirm tools appear under their configured group labels; separately, confirm that removing a tool from a group (a UI-only metadata change) has no effect on whether the tool passes server-side gating or is invocable.

**Acceptance Scenarios**:

1. **Given** the catalog of registered tools, **When** a user views the tool/extension picker, **Then** tools are displayed grouped by their configured category (e.g., Basic, Web, Microsoft 365).
2. **Given** a tool's group assignment is changed, **When** the change is made, **Then** it affects only UI presentation — the tool's server-side gating evaluation (User Story 1) and enablement re-validation (User Story 4) are unaffected.
3. **Given** a tool that fails server-side gating for the current caller/model, **When** the picker renders, **Then** the tool is either omitted or clearly non-selectable within its group — grouping never implies availability.

### Edge Cases

- What happens when a tool is removed from the catalog entirely while still marked enabled in a user's persisted preferences — is stale enablement state cleaned up, or silently ignored on next read?
- How does gating evaluation behave when a persona itself restricts tool use (e.g., a persona-level tool allow-list) in addition to the four platform-level gating layers — do persona-level restrictions compose with (narrow further) or override platform gating?
- What happens when `allowedModels` for a tool is empty/unset — does the tool default to available-on-all-models, or unavailable-until-configured?
- What is the correct behavior when a request specifies a mid-conversation model switch (e.g., via persona fallback) after tools were already resolved for the turn — are tools re-gated against the new model before the next tool call?
- How does the final-answer control tool interact with a turn where every catalog tool is disabled — is it still exposed so the model can produce a normal response, or omitted entirely when no other tools are present?
- What happens when the same tool name is registered with conflicting gating metadata in two different configuration sources (e.g., environment override vs. stored config) — which source wins, consistent with the resolution order spec 014 already defines for model allow-lists?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow the model to invoke registered server-side tools during response generation and MUST render their results back into the conversation.
- **FR-002**: Every registered tool MUST carry gating metadata consisting of at least `requiresAdmin`, `isDemo`, `requiresAdvancedModel`, and `allowedModels`.
- **FR-003**: The system MUST evaluate `requiresAdmin` server-side for every request, excluding the tool from the model-facing tool set when the caller is not an admin, regardless of any client-supplied enablement state.
- **FR-004**: The system MUST evaluate `requiresAdvancedModel` server-side for every request, excluding the tool from the model-facing tool set when the active model lacks tool/function-calling capability.
- **FR-005**: The system MUST evaluate each tool's `allowedModels` allow-list server-side for every request, excluding the tool from the model-facing tool set when the active model is not on that tool's list.
- **FR-006**: `isDemo` MUST affect only UI visibility/labeling and MUST NOT be used, alone or in combination with client-supplied state, as a substitute for the `requiresAdmin`, `requiresAdvancedModel`, or `allowedModels` server-side checks.
- **FR-007**: Tool-availability resolution (FR-003–FR-006) MUST be performed identically whether the request originates from the standard chat UI or a direct API call, per constitution Principle II.
- **FR-008**: The tool catalog MUST include, at minimum: a knowledge-collection query tool, a per-chat document query tool, a web search/research tool, a calculator, an image-generation tool, a CSV export tool, a maps/routing tool, a weather tool, an interactive-artifact tool, and a final-answer control tool.
- **FR-009**: Each catalog tool's domain-specific behavior (authorization semantics, output format, isolation guarantees) is owned by its respective domain spec (spec 004 for chat-scoped tools including CSV export, image generation, and artifacts; spec 012/019 for knowledge-collection/data-product query; spec 007/008 for Canvas-context tools) and MUST NOT be re-implemented or re-specified by this spec.
- **FR-010**: Every tool call MUST run under an execution timeout (default per spec 004 FR-009: 30s, overridable per-tool up to a platform-wide maximum of 150s); a tool call exceeding its timeout MUST return a "timed out" result to the model rather than hanging the response stream.
- **FR-011**: Tool failure payload shape (including timeout-as-failure) MUST conform to the single structured shape spec 004 (FR-011/FR-012) defines for all tools; this spec MUST NOT introduce a second or divergent failure envelope.
- **FR-012**: The system MUST persist per-user and per-persona tool enablement (which registered tools are turned on) across sessions.
- **FR-013**: WHEN a chat request is processed, THE SYSTEM MUST re-read the current persisted tool enablement state rather than trusting a client-cached snapshot.
- **FR-014**: A tool MUST be exposed to the model only when it is both (a) enabled in the current persisted per-user/per-persona state AND (b) passing all four gating layers (FR-003–FR-006) at request time — persisted enablement alone is never sufficient.
- **FR-015**: The tool/extension picker UI MUST group tools into categories (e.g., Basic, Web, Microsoft 365) for discoverability.
- **FR-016**: Tool group/category assignment MUST be UI-presentation metadata only and MUST NOT influence server-side gating (FR-003–FR-006) or enablement re-validation (FR-013–FR-014) outcomes.

### Key Entities *(include if feature involves data)*

- **ToolDefinition**: a registered server-side tool ("extension") — name, description, execution handler, UI group/category, and gating metadata (`requiresAdmin`, `isDemo`, `requiresAdvancedModel`, `allowedModels`, timeout override). The catalog (FR-008) is the concrete set of `ToolDefinition` instances the platform ships with.
- **ToolEnablementState**: persisted per-user/per-persona record of which registered tools are turned on; re-read (not cached) on every request per FR-013.
- **ToolGatingResult**: the per-request, per-tool outcome of evaluating all four gating layers against the caller, active model, and `ToolDefinition` metadata — the server-authoritative answer to "is this tool exposable to the model right now," independent of `ToolEnablementState`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of requests from non-admin callers exclude every `requiresAdmin` tool from the model-facing tool set, across a test corpus covering both UI-originated and direct-API requests.
- **SC-002**: 100% of requests using a model outside a tool's `allowedModels` list exclude that tool, and 100% of requests using a model lacking tool-calling capability exclude every `requiresAdvancedModel` tool.
- **SC-003**: 0 instances across the test corpus where a client-supplied enablement flag or `isDemo` flag alone results in a tool being exposed that fails any server-side gating layer.
- **SC-004**: 100% of the nine catalog tool categories plus the final-answer control tool (FR-008) are invocable in at least one passing end-to-end test each.
- **SC-005**: 100% of forced tool timeouts return a "timed out" result to the model without hanging the stream, and 100% of resulting failure payloads match the single structured shape defined in spec 004.
- **SC-006**: 100% of persisted tool-enablement changes are reflected on the very next request with no reliance on session restart, across a test corpus of enable/disable toggles.
- **SC-007**: 100% of simulated gating-condition changes (role revoked, model changed) made after a tool was enabled in stored state result in that tool being excluded on the next request, with 0 stale-enablement bypasses.
- **SC-008**: 100% of tool-group reassignments in the test corpus produce zero change in server-side gating or enablement outcomes for the reassigned tool.

## Assumptions

- This spec covers the cross-cutting gating framework, execution-timeout contract, enablement-persistence contract, minimum tool catalog list, and UI grouping for tools/extensions. It does not re-specify: chat-scoped tool mechanics and failure-shape consistency (owned by spec 004, User Story 4 and FR-009/FR-011/FR-012, and User Story 2 for artifacts), knowledge-collection/data-product query authorization (owned by spec 012/019), Canvas-context tool behavior (owned by spec 007/008), A2A agent invocation (owned by spec 011), or model allow-list resolution mechanics (owned by spec 014). Where this spec's requirements reference those behaviors (e.g., FR-010's timeout default, FR-011's failure shape), it does so by cross-reference rather than duplication.
- The four gating layers (`requiresAdmin`, `isDemo`, `requiresAdvancedModel`, `allowedModels`) are additive/AND-combined: a tool must pass every applicable layer to be exposed. This spec does not introduce a mechanism for one layer to override or waive another.
- Per constitution Principle II, `isDemo` is explicitly UI-visibility-only and is never treated as a security boundary anywhere in this spec's requirements, distinguishing it from the three server-enforced layers.
- Per-persona tool restrictions beyond the four platform-level gating layers (e.g., a persona author limiting which of the enabled tools that persona may use) are treated as a form of the enablement state in User Story 4 rather than a fifth gating layer; if persona-level tool curation needs richer semantics than on/off, that is deferred to whichever persona-authoring spec (009/010) owns persona configuration, not re-specified here.
- The specific set of "Microsoft 365" or other UI group names, and the exact assignment of each catalog tool to a group, is a UI/product decision left to implementation; this spec only mandates that grouping exists and is non-authoritative.
- Execution timeout defaults and the platform-wide maximum override (30s/150s) are inherited unchanged from spec 004 FR-009; this spec does not redefine those numbers, only the requirement that every catalog tool (not only the six enumerated in spec 004) is subject to the same timeout contract.
