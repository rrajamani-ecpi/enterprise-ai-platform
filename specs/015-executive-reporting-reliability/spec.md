# Feature Specification: Executive Reporting Reliability

**Feature Branch**: `015-executive-reporting-reliability`

**Created**: 2026-07-20

**Status**: Draft

**Input**: Derived from SSD_Document.md §3.8 (Admin / Settings / Executive Reporting — Executive Dashboard material only) and §5 (Architectural Debt, "Silent failure-masking in reporting paths") — reframed from "as-is" discovery findings into target requirements. Source facts: on any query error, the executive-stats service masks the failure and returns `status:"OK"` with hardcoded placeholder numbers; the page component has its own second, different hardcoded fallback for the same failure mode — two inconsistent "it broke" datasets for one condition; the dashboard is gated to `isAdmin OR advancedModelAccess`; it is served from a Cosmos-cached document and recomputed via several sequential (artificially delayed, to avoid Cosmos rate limits) queries when stale; growth percentages are clamped to 0 rather than shown negative. A fully-specified, larger `ExecutiveDashboardStats` type (hockey-stick growth, team comparisons, board export) exists with zero implementing service and is out of scope here.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A genuine error surfaces when executive stats fail to load (Priority: P1)

An admin or `advancedModelAccess` user opens the executive dashboard while the underlying stats query is failing (e.g., a Cosmos error).

**Why this priority**: This is the critical bug driving the whole spec. Today a query failure is silently converted into `status:"OK"` with hardcoded placeholder numbers in the service, and the page component has its own *second, different* hardcoded fallback for the same condition — meaning an admin viewing a broken dashboard sees confident-looking numbers with no indication anything is wrong, and the two fallback datasets don't even agree with each other. Nothing else in this spec is meaningful until a real failure produces a real, visible error state.

**Independent Test**: Force the underlying executive-stats query to fail, load the dashboard, and confirm it shows a visible "data unavailable" indicator with no numeric values rendered — then confirm there is exactly one error-handling implementation left (not one in the service and a different one in the page component).

**Acceptance Scenarios**:

1. **Given** the executive-stats service's underlying query fails, **When** the dashboard requests stats, **Then** the service returns a genuine error result (not `status:"OK"`) and includes no placeholder numbers.
2. **Given** the service returns an error result, **When** the page renders, **Then** the UI shows a visible "data unavailable" indicator in place of any statistic, chart, or number.
3. **Given** the two current independent hardcoded fallback datasets (one in the service, one in the page component), **When** this fix ships, **Then** only one real error path remains, exercised consistently by both layers.

---

### User Story 2 - Dashboard access stays restricted to authorized roles (Priority: P2)

A user without elevated access attempts to view the executive dashboard, either through the UI or by calling the underlying stats endpoint directly.

**Why this priority**: This gate (`isAdmin OR advancedModelAccess`) already works correctly today. It's included here — ranked below the P1 fix — as a regression guard: consolidating two failure-handling code paths into one (Story 1) must not loosen who can reach either the UI or the underlying data.

**Independent Test**: Attempt to load the dashboard and call its underlying stats endpoint directly as a user with neither `isAdmin` nor `advancedModelAccess`; confirm both are denied. Then confirm an admin and a non-admin `advancedModelAccess` user can both load it successfully.

**Acceptance Scenarios**:

1. **Given** a user with `isAdmin:false` and `advancedModelAccess:false`, **When** they navigate to the executive dashboard, **Then** access is denied.
2. **Given** a user with `isAdmin:true`, **When** they navigate to the executive dashboard, **Then** it renders.
3. **Given** a user with `advancedModelAccess:true` and `isAdmin:false`, **When** they navigate to the executive dashboard, **Then** it renders.
4. **Given** a user with neither flag, **When** they call the underlying stats endpoint directly (bypassing the UI), **Then** the request is rejected the same as UI-level navigation.

---

### User Story 3 - Stats are served from cache and refreshed without overloading Cosmos (Priority: P3)

A user loads the dashboard while the cached stats document is fresh, and separately while it is stale.

**Why this priority**: This caching/recompute behavior (Cosmos-cached document, sequential paced queries on staleness to stay under Cosmos rate limits) already works correctly today. It's included as a regression guard because the recompute path is exactly where the masked failure in Story 1 occurs — the fix must not disturb the caching/pacing behavior around it.

**Independent Test**: With a fresh cached stats document present, load the dashboard and confirm no recompute queries fire. Then force staleness and confirm the recompute path issues its underlying queries sequentially (not concurrently) and persists a fresh cached document on success.

**Acceptance Scenarios**:

1. **Given** a non-stale cached stats document exists, **When** a user loads the dashboard, **Then** cached values are returned without recomputation.
2. **Given** the cached document is stale, **When** a user loads the dashboard, **Then** the system recomputes by issuing the underlying queries sequentially and updates the cache on success.
3. **Given** the recompute sequence is running, **When** its queries execute, **Then** they are paced rather than fired concurrently.

### Edge Cases

- What happens if the cache write after a successful recompute itself fails? Freshly computed real values must still reach the UI (logged, non-blocking) — this must not trip the Story 1 "data unavailable" path, since the underlying stats did succeed.
- What happens when only some of the sequential recompute queries fail partway through? Today this scenario is one of the cases masked into a fake `OK`. Under this spec, any single query failure in the sequence fails the whole recompute — the system must not surface a partial result mixing real and placeholder values.
- A user without dashboard access calls the underlying stats API directly rather than navigating the UI — must be rejected identically to UI-level gating (see Story 2, Scenario 4).
- Growth-percentage clamping to 0: retained as current behavior (see Requirements and Assumptions) — this applies to genuinely computed values and is a separate concern from the error-masking bug, not part of the Story 1 fix.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST NOT return a fabricated success response when the underlying executive-stats query fails; a query failure MUST be represented as a genuine error/unavailable result, never `status:"OK"` with placeholder data.
- **FR-002**: WHEN executive stats are unavailable due to a query failure, THE dashboard UI MUST render a visible "data unavailable" indicator in place of any statistic, chart, or number.
- **FR-003**: Exactly one error-handling implementation for executive-stats failures MUST exist across the service and page layers; the two current independently-maintained hardcoded fallback datasets MUST be consolidated into this single real-error path.
- **FR-004**: Access to executive dashboard stats, at both the UI and the underlying API/service, MUST be restricted to users with `isAdmin:true` OR `advancedModelAccess:true`.
- **FR-005**: WHEN the cached stats document is not stale, THE system MUST serve it directly without re-running the underlying queries.
- **FR-006**: WHEN the cached stats document is stale, THE system MUST recompute stats via the underlying queries, pacing them sequentially rather than firing them concurrently, to stay within Cosmos rate limits.
- **FR-007**: On a successful recompute, THE system MUST persist a refreshed cached stats document.
- **FR-008**: A failure of any query partway through the sequential recompute MUST fail the recompute as a whole and follow the FR-001/FR-002 error path — no partial result may mix real and placeholder values.
- **FR-009**: Growth-percentage figures MUST continue to be clamped to a floor of 0 (never displayed as negative) for successfully computed values.

### Key Entities *(include if feature involves data)*

- **Executive Stats Cache Document**: the Cosmos-cached snapshot of dashboard metrics, with a staleness marker; read directly when fresh, recomputed and rewritten when stale.
- **Executive Stats Result**: the envelope returned by the stats service — either a real success payload or a genuine error state; must never encode a failure as a disguised success.
- **Executive Dashboard UI**: the page component that gates on `isAdmin OR advancedModelAccess` and renders either the stats payload or the "data unavailable" indicator.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of injected executive-stats query failures result in a visible "data unavailable" UI state; 0% return any placeholder numeric value.
- **SC-002**: Exactly one code path implements executive-stats error/fallback handling (service and UI consolidated), confirmed by code audit — down from two today.
- **SC-003**: 0% of dashboard load attempts (UI or direct API) by users lacking both `isAdmin` and `advancedModelAccess` succeed, across a test suite covering both surfaces.
- **SC-004**: 100% of dashboard loads against a fresh cache avoid re-issuing the underlying recompute queries, verified by query-count assertions in tests.
- **SC-005**: 100% of stale-cache recomputes execute their underlying queries sequentially with no concurrent overlap, verified by test instrumentation.

## Assumptions

- The larger, fully-specified `ExecutiveDashboardStats` type (hockey-stick growth, team comparisons, board export) has zero implementing service today. It is dead/aspirational design and explicitly out of scope for this spec; it could become its own future spec once the reliability gap here is closed, but is not treated as a current requirement.
- Growth-percentage clamping to 0 (rather than showing negative growth) is retained as-is. Reasoning: this clamps genuinely computed values and is a deliberate presentational choice, not a data-integrity defect like the error-masking bug — changing it would be a separate product decision (whether executives should see decline at all) requiring stakeholder input, not a reliability fix. A future spec could revisit this if signed growth values are desired.
- The "data unavailable" indicator is a partial/UI-level state, not a full page crash — the rest of the dashboard shell (navigation, layout) is expected to continue rendering around it.
- The specific staleness threshold and the recompute queries' pacing/delay values are retained as implementation details; this spec requires only that recompute queries remain sequential/paced, not that their timing changes.
- "Users can view the executive dashboard" throughout this spec refers only to `isAdmin` or `advancedModelAccess` users; the admin/model-config/user-preferences material in §3.8 is covered by a separate spec.
