# Feature Specification: A2A Agent Invocation Contract

**Feature Branch**: `011-a2a-agent-invocation-contract`

**Created**: 2026-07-20

**Status**: Draft

**Input**: Derived from SSD_Document.md §3.6 (Orchestration & Agents (A2A)) — the external Agent-to-Agent invocation surface only. Source facts: `/api/agents/[id]` requires header `x-agent-api-key`, constant-time-compared against the persona's own `apiKey`, failing closed (401/404/403) for a missing key, missing persona, or `a2aEnabled === false`; the AI-memory `userId` is derived from the caller's context ID rather than the persona ID, preventing cross-caller memory bleed when multiple external callers reuse the same persona-as-agent; an `AgentCard` (`@a2a-js/sdk`) describes each enabled persona (`name`, `description`, `url: /api/agents/{personaId}`, `capabilities`, `skills[]` — one per persona extension); and production execution is fully delegated to an external Azure Logic Apps webhook (trigger/cancel calls only — the step-engine itself is not in this repository). This spec covers only the A2A external invocation surface from §3.6; the workflow-builder's save/authorization/cycle-detection concerns from the same section are covered by `specs/001-orchestration-builder/spec.md` and are out of scope here.

Merged with `docs/PRODUCT_REQUIREMENTS_DOCUMENT.md` §4.13 (Agents & Integrations — A2A/MCP), per `docs/prd-decomposition-plan.md`'s routing of REQ-AGENT-1..4 to this spec. REQ-AGENT-1 (publish personas as A2A agents, API-key-authenticated) is already fully covered above (User Stories 1–4) — no new content added for it. REQ-AGENT-2 (consuming external MCP servers/tools to extend assistant capabilities) is a new capability direction not previously covered here — see User Story 5. REQ-AGENT-3 (exposing internal resources, e.g. data products, via MCP with authorization) is cross-referenced to `specs/019-data-products-core/spec.md` (REQ-DP-7) rather than re-specified here, since this spec's scope is the invocation/monitoring contract, not what gets exposed. REQ-AGENT-4 (MCP interaction logging and an admin monitoring dashboard) is a new gap — see User Story 6.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - External caller invokes a persona as an A2A agent (Priority: P1)

An external system holds a persona's A2A API key and calls `/api/agents/{personaId}` to have that persona process a request, exactly as it would call any other agent in an A2A-compatible ecosystem.

**Why this priority**: This is the entire point of the surface — without a working, correctly-authenticated invocation path, there is no A2A feature. Everything else in this spec (memory isolation, discoverability, execution delegation) only matters once a call can succeed end-to-end.

**Independent Test**: With a persona that has `a2aEnabled: true` and a known `apiKey`, call `/api/agents/{personaId}` with a valid `x-agent-api-key` header and confirm a successful agent response is returned.

**Acceptance Scenarios**:

1. **Given** a persona with `a2aEnabled: true` and a provisioned `apiKey`, **When** an external caller sends `x-agent-api-key` matching that key, **Then** the request is accepted and the persona responds.
2. **Given** the same persona, **When** the header is present but does not match the persona's `apiKey`, **Then** the system rejects the request with 403 and performs no agent invocation.
3. **Given** the same persona, **When** the header is omitted entirely, **Then** the system rejects the request with 401 and performs no agent invocation.
4. **Given** a `personaId` that does not resolve to any persona, **When** any caller invokes it, **Then** the system rejects the request with 404 regardless of any header supplied.
5. **Given** a persona that exists and has a valid `apiKey` but `a2aEnabled: false`, **When** a caller supplies the correct key, **Then** the system rejects the request with 403.

---

### User Story 2 - Per-caller memory isolation across shared personas (Priority: P1)

Two different external systems both call the same persona-as-agent over time, each maintaining their own conversational context, and neither should ever see the other's conversation history or state bleed into their own.

**Why this priority**: This is a data-isolation guarantee, not a convenience feature. A persona reused as an agent by multiple external callers is an expected, supported configuration; without correct isolation, one caller's private exchange could leak into another caller's session, which is a security/privacy defect as serious as an authentication bypass. It ranks alongside Story 1 because the invocation path is not safely shippable without it.

**Independent Test**: Invoke the same persona-as-agent from two distinct caller contexts (distinct A2A `contextId` values) with different conversational content, then confirm each caller's subsequent call only ever surfaces its own prior context, never the other caller's.

**Acceptance Scenarios**:

1. **Given** two external callers, A and B, invoking the same persona with distinct `contextId` values, **When** each sends a message referencing information only it previously shared, **Then** the persona's response to A never reflects information only B shared, and vice versa.
2. **Given** a single external caller reusing the same `contextId` across multiple calls, **When** it sends a follow-up message, **Then** the persona's stored memory/state for that `contextId` is retained and available (isolation does not break continuity for the same caller).
3. **Given** the AI-memory `userId` used internally, **When** inspected for any given call, **Then** it is derived from the caller's `contextId`, never from the `personaId` alone — so memory partitioning survives regardless of how many external callers share the same persona.

---

### User Story 3 - Discover a persona's agent capabilities via AgentCard (Priority: P2)

An external A2A-ecosystem caller fetches a persona's `AgentCard` to discover its name, description, capabilities, and skills before deciding how (or whether) to invoke it.

**Why this priority**: Discoverability is important for A2A-ecosystem interoperability (callers commonly probe an agent's card before invoking it) but is not required for a caller that already knows out-of-band which persona/key to use. It's ranked below the invocation and isolation guarantees because a caller can still function without ever fetching the card.

**Independent Test**: Request the AgentCard for an `a2aEnabled` persona and confirm it accurately reflects that persona's name, description, `url`, and one skill entry per configured extension.

**Acceptance Scenarios**:

1. **Given** a persona with `a2aEnabled: true` and two configured extensions, **When** its AgentCard is requested, **Then** the returned card's `skills[]` contains exactly one entry per configured extension, and `url` resolves to `/api/agents/{personaId}`.
2. **Given** a persona with `a2aEnabled: false`, **When** its AgentCard is requested, **Then** the system does not expose a usable card for invocation (consistent with the fail-closed behavior of the invocation endpoint itself).

---

### User Story 4 - Production execution delegates to external Logic Apps (Priority: P3)

An A2A-invoked persona's actual step-by-step execution in production runs inside an external Azure Logic Apps workflow rather than in-process, with this repository only responsible for triggering and cancelling that external run.

**Why this priority**: This is an architectural boundary/integration-contract concern rather than a user-facing behavior difference — from the external caller's point of view, Story 1 already covers the observable request/response contract. It's ranked lowest because it constrains *how* execution happens operationally, not *whether* the invocation surface behaves correctly.

**Independent Test**: Trigger an A2A invocation in a production-configured environment and confirm this repository issues an authenticated webhook trigger call to the configured Logic Apps endpoint (and a corresponding cancel call when applicable), without attempting to run the step-engine in-process.

**Acceptance Scenarios**:

1. **Given** a production-configured environment, **When** an A2A invocation proceeds to execution, **Then** the system issues an authenticated trigger call to the external Logic Apps webhook and does not execute the step-engine in-process.
2. **Given** a local development environment, **When** an A2A invocation proceeds to execution, **Then** the system runs the engine in-process instead of calling the external webhook (dev/prod parity is intentionally not required at this layer).
3. **Given** an in-flight external execution, **When** a cancellation is requested, **Then** the system issues an authenticated cancel call to the same external webhook.

### User Story 5 - Consume external MCP servers/tools to extend assistant capabilities (Priority: P2)

An administrator registers an external MCP server, and a persona/assistant execution can then discover and invoke that server's exposed tools mid-conversation to extend its own capabilities beyond what's built into the platform.

**Why this priority**: This is a new capability direction (the platform acting as an MCP *host/client*, not just an A2A server) that materially expands what personas can do, but it is additive to — not required for — the already-shipped invocation/isolation/discovery guarantees in Stories 1–4.

**Independent Test**: Register an external MCP server, enable it for a persona, invoke that persona with a request requiring the external tool, and confirm the response incorporates the external tool's output.

**Acceptance Scenarios**:

1. **Given** an external MCP server registered with valid connection details, **When** an authorized admin enables it, **Then** its exposed tools become available for persona configuration.
2. **Given** a persona configured to use an enabled external MCP tool, **When** invoked with a request that requires that tool, **Then** the system calls the external tool and incorporates its result into the response.
3. **Given** an external MCP server that is unreachable or errors, **When** a persona attempts to use one of its tools, **Then** the system surfaces a distinguishable failure rather than silently omitting the capability or hanging indefinitely.
4. **Given** an external MCP server that has been disabled or removed by an admin, **When** a persona previously configured to use it is invoked, **Then** the system does not attempt to call it and fails closed/gracefully instead.

---

### User Story 6 - Admin monitoring of MCP activity (Priority: P2)

An administrator opens an MCP monitoring dashboard to see a log of MCP interactions — both external tool calls the platform consumed and internal resources the platform exposed — to audit usage and diagnose failures.

**Why this priority**: This is an operational/governance capability that depends on MCP traffic existing to observe (Story 5 and REQ-AGENT-3's exposure path), but it is essential for safely operating an expanding external-integration surface, so it ranks alongside Story 5 rather than as an afterthought.

**Independent Test**: Trigger several MCP interactions in both directions (consuming and exposing), then confirm the admin dashboard shows a corresponding entry for each, distinguishing success from failure.

**Acceptance Scenarios**:

1. **Given** an MCP interaction occurs (consuming an external tool or serving an internal resource via MCP), **When** it completes, **Then** a log entry is recorded capturing at least timestamp, direction, resource/tool identity, and outcome.
2. **Given** an admin opens the MCP monitoring dashboard, **When** viewing it, **Then** they see recent MCP interactions across both consumption and exposure directions.
3. **Given** a failed MCP interaction, **When** viewed in the dashboard, **Then** it is visibly distinguishable from a successful one with enough detail to diagnose the failure.
4. **Given** a non-admin user, **When** they attempt to access the MCP monitoring dashboard, **Then** access is denied.

### Edge Cases

- What happens when the external Logic Apps trigger call itself fails (network error, non-2xx response, timeout)? The system MUST surface this as a distinguishable execution failure to the caller rather than reporting success, and MUST NOT leave the invocation in an ambiguous "still running" state with no recorded outcome.
- What happens when two calls arrive concurrently with the same `contextId` from the same caller? Both must resolve against the same isolated memory partition without cross-corrupting each other's writes.
- What happens when a persona's `apiKey` is rotated while a caller is mid-session? The next call with the old key must fail closed (403), independent of any in-flight or previously-successful calls.
- How does the system respond when the `x-agent-api-key` header is present but empty, or duplicated (sent twice with different values)? Both must be treated as a non-matching key, not as a missing key or first-match-wins.
- What happens when a persona is deleted or has `a2aEnabled` toggled off while an external caller has cached its `apiKey`/AgentCard? The next invocation must re-evaluate current persona state, not cached state, and fail closed.
- What happens when an external MCP server's credentials expire or are revoked mid-session? The next tool-call attempt must fail closed and be logged as a failure, not silently retried indefinitely.
- What happens when MCP interaction volume is high? Logging and the monitoring dashboard MUST NOT silently drop entries or degrade to a state where interactions go unrecorded.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `/api/agents/{personaId}` MUST require an `x-agent-api-key` header on every invocation request.
- **FR-002**: The system MUST reject the request with 401 when the `x-agent-api-key` header is missing or empty.
- **FR-003**: The system MUST reject the request with 404 when `personaId` does not resolve to an existing persona.
- **FR-004**: The system MUST reject the request with 403 when the supplied key does not match the resolved persona's own `apiKey`, using a constant-time comparison to avoid timing side-channels.
- **FR-005**: The system MUST reject the request with 403 when the resolved persona has `a2aEnabled: false`, even if the supplied key is otherwise correct.
- **FR-006**: All three failure modes (missing/invalid key, missing persona, disabled A2A) MUST fail closed before any agent invocation, memory read, or memory write occurs.
- **FR-007**: The system MUST derive the AI-memory `userId` used for a given A2A invocation from the caller's A2A `contextId`, never from the `personaId` alone.
- **FR-008**: Two distinct `contextId` values invoking the same persona MUST resolve to non-overlapping memory partitions; a repeated call with the same `contextId` MUST resolve to the same memory partition as its prior calls.
- **FR-009**: The system MUST expose an `AgentCard` for each `a2aEnabled` persona containing `name`, `description`, `url` (`/api/agents/{personaId}`), `capabilities`, and `skills[]` with exactly one entry per configured persona extension.
- **FR-010**: The system MUST NOT expose a usable AgentCard/invocation path for a persona with `a2aEnabled: false`.
- **FR-011**: In a production-configured environment, execution triggered via an A2A invocation MUST be delegated to the external Azure Logic Apps webhook via an authenticated trigger call, and cancellation MUST be delegated via an authenticated cancel call; the in-repository step-engine MUST NOT run in this configuration.
- **FR-012**: In a local development environment, execution triggered via an A2A invocation MUST run using the in-process engine rather than calling the external webhook.
- **FR-013**: The system MUST surface a distinguishable failure (not a silent success or an indefinite pending state) when the external Logic Apps trigger call itself fails.
- **FR-014**: The platform MUST support acting as an MCP host, capable of connecting to external MCP servers to consume their exposed tools.
- **FR-015**: The system MUST allow authorized admins to register and configure external MCP server connections (e.g., endpoint, credentials) and to enable/disable them.
- **FR-016**: The system MUST make enabled, consumed external MCP tools available for use during persona/assistant execution.
- **FR-017**: The system MUST surface a distinguishable failure when an external MCP server or tool call fails, is unreachable, or has been disabled/removed, rather than silent omission or an indefinite hang.
- **FR-018**: The system MUST log every MCP interaction — both consuming external MCP tools and serving internal resources via MCP (per REQ-AGENT-3 / `specs/019-data-products-core/spec.md`) — capturing at minimum timestamp, direction, resource/tool identity, and outcome.
- **FR-019**: The system MUST provide an admin-only monitoring view surfacing logged MCP interactions across both consumption and exposure directions.
- **FR-020**: The system MUST restrict access to the MCP monitoring dashboard to authorized admins, denying access to all other users.
- **FR-021**: The MCP monitoring dashboard MUST allow an admin to distinguish failed MCP interactions from successful ones with sufficient detail to diagnose the failure.

### Key Entities *(include if feature involves data)*

- **AgentCard** (`@a2a-js/sdk`): externally-facing descriptor of a persona-as-agent — `name`, `description`, `url` (`/api/agents/{personaId}`), `capabilities`, `skills[]` (one per persona extension). Only produced for `a2aEnabled` personas.
- **Persona A2A credential**: the persona's own `apiKey`, compared constant-time against the caller-supplied `x-agent-api-key` header; gated by the persona's `a2aEnabled` flag.
- **A2A caller context (`contextId`)**: the external caller-supplied identifier that the AI-memory `userId` is derived from, providing per-caller memory partitioning independent of which persona is being invoked.
- **External MCP server connection**: an admin-registered, enable/disable-able connection to an external MCP server (endpoint, credentials) whose exposed tools become available for persona/assistant use.
- **MCP interaction log entry**: a record of one MCP interaction (consumed external tool call, or served internal resource — the latter's content owned by `specs/019-data-products-core/spec.md`), capturing timestamp, direction, resource/tool identity, and outcome; the basis for the admin monitoring dashboard.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of invocation requests missing the `x-agent-api-key` header are rejected with 401, across a test corpus including empty-string and duplicated-header variants.
- **SC-002**: 100% of invocation requests against a nonexistent `personaId` are rejected with 404, regardless of header content.
- **SC-003**: 100% of invocation requests with a non-matching key, or targeting a persona with `a2aEnabled: false`, are rejected with 403, with 0 agent invocations, memory reads, or memory writes occurring for any rejected request.
- **SC-004**: 0 instances of cross-`contextId` memory bleed across a test matrix of ≥2 distinct callers sharing one persona over ≥3 sequential calls each.
- **SC-005**: 100% of AgentCard requests for `a2aEnabled` personas return a `skills[]` array whose length equals the persona's configured extension count.
- **SC-006**: 100% of simulated external Logic Apps trigger-call failures result in a distinguishable failure outcome recorded for the invocation, with 0 cases reported as success or left with no recorded outcome.
- **SC-007**: 100% of external MCP servers that pass admin-side connectivity/health checks appear as available tool sources for persona configuration.
- **SC-008**: 100% of simulated external MCP tool-call failures (unreachable server, error response, disabled/removed server) result in a distinguishable failure outcome, with 0 cases silently omitted or left pending indefinitely.
- **SC-009**: 100% of MCP interactions, in both directions (consumed external tools and exposed internal resources), produce a corresponding log entry visible in the admin monitoring dashboard.
- **SC-010**: 100% of access attempts to the MCP monitoring dashboard by non-admin users are denied.

## Assumptions

- The external Azure Logic Apps step-engine itself (its internal workflow steps, retry semantics, and execution logic once triggered) is out of scope for this spec — this repository is responsible only for the authenticated trigger/cancel calls to that external webhook, not for the engine's internal correctness.
- The graph-shape, authorization, and cycle-detection concerns for orchestration graphs themselves (the persona-to-persona delegation workflow that an A2A-invoked execution may run) are covered by `specs/001-orchestration-builder/spec.md` and are not restated or modified here.
- "Local development environment" vs. "production-configured environment" is an existing, externally-controlled deployment distinction (e.g., environment configuration/feature flag); this spec assumes that distinction already exists and only specifies the differing behavior across it.
- Persona extensions (the source of `skills[]` entries) are defined and managed by Persona Management (Domain E); this spec treats the current set of configured extensions as a given input, not something it governs.
- REQ-AGENT-3 (exposing internal resources, e.g., data products/knowledge collections, via MCP with authorization) is expected to be specified in `specs/019-data-products-core/spec.md`, which does not yet exist at the time of this merge. This spec only owns the logging/monitoring contract (FR-018–FR-021) for whatever gets exposed there — it is a forward pointer, not a substitute, and should be reconciled once spec 019 lands.
- "MCP host" in FR-014–FR-017 refers to the platform consuming/calling tools on external MCP servers (client role), distinct from the platform's own MCP-server exposure of internal resources, which is REQ-AGENT-3's concern.
