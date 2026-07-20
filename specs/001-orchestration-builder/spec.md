# Feature Specification: Orchestration Builder — Persisted, Authorized Graph Editing

**Feature Branch**: `001-orchestration-builder`

**Created**: 2026-07-20

**Status**: Draft

**Input**: Derived from SSD_Document.md §3.6 (Orchestration & Agents) and §5 (Architectural Debt) — reframed from "as-is" discovery findings into target requirements. Source facts: the workflow-builder UI's "create" flow never attaches drawn nodes/connections to the create call (canvas ref not wired up, so anything drawn is silently discarded), the "view existing orchestration" page renders an empty fragment, `EnsureOrchestrationOperation` falls through to an "OK" response for callers who are neither owner nor admin, and no cycle detection exists at any layer — despite the execution engine underneath being fully implemented and unit-tested.

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

### Edge Cases

- What happens when a save request omits a terminal node entirely? (Existing graph-shape rule — at least one trigger and one terminal node, single connected component — must continue to be enforced alongside the new checks in this spec, not replaced by them.)
- How does the system handle a save request for an orchestration that was deleted by another caller between page load and save?
- What happens when a cycle spans more than two nodes, or when multiple disjoint cycles exist in the same graph?
- How does the view/edit page behave for an orchestration that exists but the current caller is not authorized to see (should not distinguish "not found" from "forbidden," consistent with this codebase's existing ambiguous-403 convention elsewhere)?

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

### Key Entities *(include if feature involves data)*

- **OrchestrationModel**: an owned, versioned workflow graph — `name`/`description`, `nodes`, `connections`, `entryNode`, `terminalNodes`. Version increments on graph change.
- **OrchestrationNode**: typed graph node (`trigger` | `persona` | `terminal`); trigger nodes carry a required `triggerType`.
- **OrchestrationConnection**: a directed edge between two nodes; may carry a `condition` (modeled in the schema; branching/fan-out execution semantics are out of scope for this spec).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of orchestrations saved through the builder UI render an identical node/connection count on reload.
- **SC-002**: 0 non-owner/non-admin/non-collaborator mutation requests succeed against the direct API in the authorization test suite.
- **SC-003**: 100% of graphs containing a cycle are rejected at save time across a test corpus of single-cycle, multi-cycle, and no-cycle graphs, with 0 false-positive rejections on valid acyclic graphs.
- **SC-004**: 100% of direct-API saves with a missing node-type-required field are rejected server-side, independent of client-side state.

## Assumptions

- The existing execution engine (the graph traversal/delegation logic already described in SSD_Document.md §3.6) is retained as-is; this spec covers the builder's save/load/authorization/validation surface, not execution semantics.
- Branching/fan-out execution (using the schema's `condition` field on connections) remains out of scope — this spec only requires that such graphs can be saved and validated, not that they execute with branching.
- Production execution via external Azure Logic Apps is unaffected by this spec; it consumes whatever valid graph shape this spec guarantees.
- "Collaborator with edit rights" reuses the sharing-permission model already established for personas/prompts elsewhere in the codebase, rather than introducing a new one.
