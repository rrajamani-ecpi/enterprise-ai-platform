# Feature Specification: Orchestration Builder — Persisted, Authorized Graph Editing

**Feature Branch**: `001-orchestration-builder`

**Created**: 2026-07-20

**Status**: Draft

**Input**: Derived from SSD_Document.md §3.6 (Orchestration & Agents) and §5 (Architectural Debt) — reframed from "as-is" discovery findings into target requirements. Source facts: the workflow-builder UI's "create" flow never attaches drawn nodes/connections to the create call (canvas ref not wired up, so anything drawn is silently discarded), the "view existing orchestration" page renders an empty fragment, `EnsureOrchestrationOperation` falls through to an "OK" response for callers who are neither owner nor admin, and no cycle detection exists at any layer — despite the execution engine underneath being fully implemented and unit-tested. Extended per `docs/PRODUCT_REQUIREMENTS_DOCUMENT.md` §4.12 (REQ-ORCH-1..5) and `docs/prd-decomposition-plan.md`, which route this feature's forward-looking gap — typed workflow triggers (API/file-upload/multi-modal) with per-trigger configuration — here as an addition alongside the original bug-driven scope.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Save and reload a workflow graph (Priority: P1)

A persona owner builds an orchestration by dragging trigger, persona, and terminal nodes onto the canvas and connecting them, saves it, and later reopens it expecting to see the same graph.

**Why this priority**: This is the baseline usability blocker. Today the "create" flow discards drawn nodes/connections entirely and the "view existing" page renders an empty fragment — the feature is non-functional end-to-end even though the execution engine underneath already works. Nothing else in this spec matters until a graph can round-trip through save/reload.

**Independent Test**: Draw a graph with at least one trigger, one persona, and one terminal node, save it, reload the page, and confirm the same nodes and connections render.

**Acceptance Scenarios**:

1. **Given** a new orchestration with a trigger node, a persona node, and a terminal node connected in sequence, **When** the user saves it, **Then** the saved record contains all drawn nodes and connections.
2. **Given** a previously saved orchestration, **When** the user navigates to its view/edit page, **Then** the full graph (nodes, connections, entry node, terminal nodes) renders exactly as saved.

---

### User Story 2 - Mutations are rejected for unauthorized callers (Priority: P1)

A caller who is neither the orchestration's owner nor an admin attempts to view, edit, or delete it directly via the API (bypassing the UI).

**Why this priority**: `EnsureOrchestrationOperation` currently falls through to an "OK" response when the caller is neither owner nor admin — every downstream mutation (start/edit/delete) trusts this as its authorization signal. This is currently believed to be masked by Cosmos partition-key scoping, but the in-code check itself enforces nothing, so it must be treated as equally urgent to Story 1: a working builder that isn't authorization-safe is not shippable.

**Independent Test**: Call the orchestration edit/delete endpoints directly (not through the UI) as a user who is neither the resource's owner nor an admin, and confirm the request is rejected before any mutation occurs.

**Acceptance Scenarios**:

1. **Given** an orchestration owned by User A, **When** User B (non-admin, non-owner, non-collaborator) issues a direct API edit request, **Then** the system rejects it and no fields are changed.
2. **Given** an orchestration owned by User A, **When** User B issues a direct API delete request, **Then** the system rejects it and the record still exists.
3. **Given** an admin caller, **When** they edit or delete any user's orchestration, **Then** the operation succeeds.

---

### User Story 3 - Cyclic graphs are rejected at save time (Priority: P2)

A user connects nodes in a way that creates a cycle (e.g., persona A → persona B → persona A).

**Why this priority**: No cycle detection exists at any layer today. A saved cyclic graph is a correctness/availability risk for the execution engine (unbounded delegation), but it doesn't block the core save/reload/authorization flows above, so it's ranked below them.

**Independent Test**: Attempt to save a graph containing a cycle and confirm the save is rejected with a specific, actionable error before any partial write occurs.

**Acceptance Scenarios**:

1. **Given** a graph where node connections form a cycle, **When** the user attempts to save, **Then** the system rejects the save and identifies which nodes form the cycle.
2. **Given** a valid acyclic graph, **When** the user saves, **Then** the save succeeds (no false positives).

---

### User Story 4 - Per-node-type fields are validated server-side (Priority: P3)

A direct API caller submits a graph where a trigger node is missing its required `triggerType`, or another node-type-specific required field is absent.

**Why this priority**: This validation currently exists only client-side and is not re-run on direct API submission — a real gap, but lower-severity than Stories 1–3 since it requires bypassing the normal UI to trigger.

**Independent Test**: Submit a graph via direct API call with a trigger node missing `triggerType` and confirm the save is rejected server-side, independent of any client-side validation.

**Acceptance Scenarios**:

1. **Given** a graph containing a trigger node with no `triggerType`, **When** submitted via direct API call, **Then** the system rejects the save with a field-specific error.
2. **Given** a graph where every node's type-specific required fields are present, **When** submitted via direct API call, **Then** the save succeeds.

---

### User Story 5 - Configure typed workflow triggers with per-trigger settings (Priority: P2)

A workflow builder adds a trigger node to a graph and selects its trigger type — API, file-upload, or multi-modal — then configures the settings specific to that type: API key, allowed origins, and rate limit for an API trigger; accepted file types, max size, max count, virus-scan requirement, and retention for a file-upload trigger; processing mode (individual/batch/combined) and context-passing mode for a multi-modal trigger.

**Why this priority**: PRD §4.12 (REQ-ORCH-2) specifies these trigger types and their configuration surface as a capability distinct from the bare `triggerType` presence check in Story 4 — today nothing in the schema or validation captures type-specific configuration at all, so this is a genuine gap rather than a bug fix. It is ranked P2 because it builds on the save/reload and authorization guarantees of Stories 1–2, and because its config values (auth, rate limits, file constraints) are themselves security/correctness-relevant, similar in urgency to cycle detection.

**Independent Test**: Create a trigger node of each type (API, file-upload, multi-modal), populate its type-specific configuration, save, reload, and confirm the configuration round-trips unchanged; separately, submit a trigger node missing a type-specific required config field via direct API call and confirm the save is rejected.

**Acceptance Scenarios**:

1. **Given** a trigger node of type `api`, **When** the user configures an API key, allowed origins, and a rate limit and saves, **Then** the saved record contains that configuration and it renders identically on reload.
2. **Given** a trigger node of type `file-upload`, **When** the user configures accepted file types, max size, max count, a virus-scan requirement, and a retention period and saves, **Then** the saved record contains that configuration and it renders identically on reload.
3. **Given** a trigger node of type `multi-modal`, **When** the user configures a processing mode (individual/batch/combined) and a context-passing mode and saves, **Then** the saved record contains that configuration and it renders identically on reload.
4. **Given** a trigger node whose type-specific required configuration field is missing (e.g., an API trigger with no rate limit, a file-upload trigger with no accepted file types), **When** submitted via direct API call, **Then** the system rejects the save with a field-specific error, consistent with the per-node-type validation in Story 4.

### Edge Cases

- What happens when a save request omits a terminal node entirely? (Existing graph-shape rule — at least one trigger and one terminal node, single connected component — must continue to be enforced alongside the new checks in this spec, not replaced by them.)
- How does the system handle a save request for an orchestration that was deleted by another caller between page load and save?
- What happens when a cycle spans more than two nodes, or when multiple disjoint cycles exist in the same graph?
- How does the view/edit page behave for an orchestration that exists but the current caller is not authorized to see (should not distinguish "not found" from "forbidden," consistent with this codebase's existing ambiguous-403 convention elsewhere)?
- What happens when a file-upload trigger's configured max size/count conflicts with a platform-wide upload limit enforced elsewhere?
- How does the system handle a trigger's type being changed on an existing node (e.g., API → file-upload) — is the prior type's configuration discarded or preserved for later restore?
- What happens when a caller without access to the feature flag attempts to reach the builder directly by URL?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The orchestration create/save flow MUST persist all nodes and connections present on the canvas at save time.
- **FR-002**: The orchestration view/edit page MUST render the full previously-saved graph (nodes, connections, entry node, terminal nodes) on load.
- **FR-003**: The system MUST reject edit and delete operations from callers who are not the orchestration's owner, a collaborator with edit rights, or an admin, before any data is mutated.
- **FR-004**: Authorization rejection MUST NOT rely solely on data-layer partition scoping — the authorization check itself must independently enforce the rule.
- **FR-005**: The system MUST detect cycles in the node/connection graph at save time and reject the save, identifying the participating nodes.
- **FR-006**: The system MUST continue to enforce the existing graph-shape rule (≥1 trigger node, ≥1 terminal node, single connected component) unchanged by the new checks in this spec.
- **FR-007**: The system MUST validate per-node-type required fields (e.g., a trigger node's `triggerType`) server-side on every save, regardless of client-side validation state.
- **FR-008**: Rejected saves (authorization, cycle, or field validation failures) MUST leave the previously-saved graph state unchanged (no partial writes).
- **FR-009**: A trigger node MUST declare one of the types `api`, `file-upload`, or `multi-modal`, and MUST carry a type-specific configuration object that is persisted and reloaded with the node.
- **FR-010**: An `api` trigger's configuration MUST support an API key, a list of allowed origins, and a rate limit.
- **FR-011**: A `file-upload` trigger's configuration MUST support accepted file types, a max file size, a max file count, a virus-scan requirement, and a retention period.
- **FR-012**: A `multi-modal` trigger's configuration MUST support a processing mode (`individual` | `batch` | `combined`) and a context-passing mode.
- **FR-013**: The system MUST validate trigger-type-specific required configuration fields server-side at save time, rejecting saves with missing/invalid fields with a field-specific error, using the same server-side enforcement point as FR-007.
- **FR-014**: The system MUST gate access to the orchestration builder (create/edit/view) behind a feature flag; when the flag is disabled for a caller, the builder MUST be inaccessible regardless of the caller's authorization role.

### Key Entities *(include if feature involves data)*

- **OrchestrationModel**: an owned, versioned workflow graph — `name`/`description`, `nodes`, `connections`, `entryNode`, `terminalNodes`. Version increments on graph change.
- **OrchestrationNode**: typed graph node (`trigger` | `persona` | `terminal`); trigger nodes carry a required `triggerType` (`api` | `file-upload` | `multi-modal`) and a type-specific **TriggerConfiguration**.
- **OrchestrationConnection**: a directed edge between two nodes; may carry a `condition` (modeled in the schema; branching/fan-out execution semantics are out of scope for this spec).
- **TriggerConfiguration**: type-specific settings attached to a trigger node — `api` (API key, allowed origins, rate limit), `file-upload` (accepted file types, max size, max count, virus-scan requirement, retention), `multi-modal` (processing mode, context-passing mode).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of orchestrations saved through the builder UI render an identical node/connection count on reload.
- **SC-002**: 0 non-owner/non-admin/non-collaborator mutation requests succeed against the direct API in the authorization test suite.
- **SC-003**: 100% of graphs containing a cycle are rejected at save time across a test corpus of single-cycle, multi-cycle, and no-cycle graphs, with 0 false-positive rejections on valid acyclic graphs.
- **SC-004**: 100% of direct-API saves with a missing node-type-required field are rejected server-side, independent of client-side state.
- **SC-005**: 100% of trigger configurations (API, file-upload, multi-modal) saved through the builder render identical configuration on reload, across a test corpus covering all three types.
- **SC-006**: 100% of direct-API saves with a missing/invalid trigger-type-specific required configuration field are rejected server-side, across all three trigger types.
- **SC-007**: 100% of builder access attempts are blocked when the feature flag is disabled, verified across owner, collaborator, and admin roles.

## Assumptions

- The existing execution engine (the graph traversal/delegation logic already described in SSD_Document.md §3.6) is retained as-is; this spec covers the builder's save/load/authorization/validation surface, not execution semantics.
- Branching/fan-out execution (using the schema's `condition` field on connections) remains out of scope — this spec only requires that such graphs can be saved and validated, not that they execute with branching.
- Production execution via external Azure Logic Apps is unaffected by this spec; it consumes whatever valid graph shape this spec guarantees.
- "Collaborator with edit rights" reuses the sharing-permission model already established for personas/prompts elsewhere in the codebase, rather than introducing a new one.
- PRD §4.12/REQ-ORCH-4 (node-by-node execution, streaming intermediate results, and persistence of execution state/per-node results/message history/snapshots) is explicitly **out of scope** for this spec: per SSD_Document.md §3.6/§5, production execution is delegated to external Azure Logic Apps whose step-engine is not present in this repository, and the in-repo execution engine used for local dev is retained as-is (see first Assumption above) rather than re-specified here. This spec covers only the graph shape and trigger configuration that such execution consumes.
- The mapping of PRD's flat trigger-configuration field list to individual trigger types (API keys/allowed origins/rate limits → `api`; file types/sizes/counts/virus-scan/retention → `file-upload`; processing mode/context-passing mode → `multi-modal`) is inferred from context, since PRD §4.12 lists these fields together without an explicit per-type mapping.
- The feature flag mechanism for gating the builder (FR-014) reuses the existing feature-flag system already used elsewhere in the codebase (e.g., for Persona Studio), rather than introducing a new one.
