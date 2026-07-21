# Feature Specification: Multi-Chat Session Persistence

**Feature Branch**: `006-multi-chat-session-persistence`

**Created**: 2026-07-20

**Status**: Draft

**Input**: Derived from SSD_Document.md §3.3 (Multi-Chat / Chat-Home / Lesson Mode) — reframed from "as-is" discovery findings into target requirements, scoped to the Multi-Chat and Chat-Home material only (Lesson Mode / Canvas submission is a separate spec). Also draws from PRODUCT_REQUIREMENTS_DOCUMENT.md §4.9 (Multi-Chat / model comparison, REQ-MULTICHAT-1), per `docs/prd-decomposition-plan.md`, which routes the base "send one message to N models and compare side-by-side" capability into this spec since prior discovery only documented the persistence and error-handling bugs around it, never the capability itself. Source facts: multi-chat quadrant count, persona assignments, and thread associations are held entirely in client-side `useState` with nothing persisted, so a page refresh loses the whole layout; if thread creation fails mid-send, the user's typed message and any attachments are silently dropped (console-log only, no user-facing error); `ChatThreadModel` already models `multiChatSessionId?`/`multiChatPosition?` fields, suggesting a persistence path was designed but never wired up.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Multi-chat layout survives a page refresh (Priority: P1)

A user opens Multi-Chat, expands to several quadrants, assigns a different persona to each, and sends messages in a couple of them — building up active threads — then refreshes the browser tab or reopens Multi-Chat later.

**Why this priority**: Today this state lives only in React `useState`; a refresh, accidental navigation, or crash silently wipes the entire working layout with no recovery. This is the single biggest usability failure in the domain and must be fixed before anything else in this spec matters.

**Independent Test**: Configure a multi-chat session with 3+ quadrants, distinct personas per quadrant, and at least one active thread with sent messages; refresh the page; confirm the same quadrant count, persona assignments, and threads (with prior messages intact) are restored.

**Acceptance Scenarios**:

1. **Given** a multi-chat session with N quadrants (2 ≤ N ≤ 4), each assigned a persona, **When** the user refreshes or reopens the page, **Then** the same quadrant count and persona assignments are restored.
2. **Given** a quadrant with an active thread and prior messages, **When** the page reloads, **Then** that quadrant reconnects to the same thread and its message history renders unchanged.
3. **Given** a user sends the first message in a previously-empty quadrant, **When** a thread is created on demand for that send, **Then** the new thread association is persisted immediately (not only held in memory) so it survives an immediate refresh.
4. **Given** a user removes a quadrant while the count is at the 2-quadrant floor, **When** the removal is attempted, **Then** the persona assignment is cleared but the slot itself is not deleted, and this cleared state persists across reload.
5. **Given** a user is at the 4-quadrant cap, **When** they attempt to add a 5th quadrant, **Then** the request is rejected and the persisted state remains at 4.

---

### User Story 2 - Failed sends surface an error and never lose the user's input (Priority: P1)

A user types a message (optionally with attachments) into an empty quadrant and hits send; the on-demand thread-creation call fails (network error, backend outage, etc.).

**Why this priority**: Today this failure path only produces a console log — the typed message and attachments vanish from the UI with no visible error, forcing the user to silently redo work with no indication anything went wrong. Paired with Story 1, this closes the two ways multi-chat currently destroys user work without telling them.

**Independent Test**: Simulate a thread-creation failure (e.g., force the create-thread endpoint to error) while sending a message with an attachment in an empty quadrant; confirm a visible error is shown and the typed message text and attachment are still present/recoverable in the input for retry.

**Acceptance Scenarios**:

1. **Given** a user sends a message in an empty quadrant, **When** the on-demand thread-creation call fails, **Then** the system displays a user-facing error in that quadrant.
2. **Given** a failed send as above, **When** the error is shown, **Then** the originally typed message text remains available (e.g., restored to the input) rather than being cleared.
3. **Given** a failed send that included one or more attachments, **When** the error is shown, **Then** those attachments remain attached/available rather than being discarded.
4. **Given** a failed send, **When** the user retries (e.g., resubmits), **Then** the system attempts thread creation again using the preserved message and attachments.

---

### User Story 3 - Chat-Home shows only starred personas (Priority: P3)

A user opens Chat-Home to quickly jump into a conversation with one of their favorite personas.

**Why this priority**: This is already-correct behavior in the current system (not a bug) but belongs in this spec since it's part of the same domain surface being formalized; it's ranked last because it carries no data-loss or reliability risk, unlike Stories 1–2.

**Independent Test**: As a user with zero starred personas, load Chat-Home and confirm the empty-state prompt appears; star one or more personas from the main Assistants page, reload Chat-Home, and confirm only those personas appear.

**Acceptance Scenarios**:

1. **Given** a user with no starred/favorite personas, **When** Chat-Home mounts, **Then** an empty state is shown prompting the user to star personas from the main Assistants page.
2. **Given** a user with one or more starred personas, **When** Chat-Home mounts, **Then** only those starred personas are listed (non-starred personas the user has access to are excluded).

---

### User Story 4 - Send one message to multiple models and compare responses side-by-side (Priority: P1)

A user opens Multi-Chat with two or more quadrants, each assigned a model/persona, types a single message once, and sends it — the message is dispatched in parallel to every quadrant's assigned model and each model's response renders in its own quadrant, side-by-side, for direct comparison.

**Why this priority**: This is the base capability the rest of this spec exists to protect — Stories 1–2's persistence and error-recovery guarantees are meaningless without the underlying parallel-query, side-by-side-comparison behavior they wrap. It is PRD-designated REQ-MULTICHAT-1 and was never previously specified; prior discovery only covered the persistence and silent-failure bugs around it.

**Independent Test**: Configure a multi-chat session with 2–4 quadrants, each assigned a different model (subject to the caller's role-based model allow-list per spec 014); type one message and send it once; confirm the same message is dispatched in parallel to every quadrant's assigned model and each quadrant renders its own response independently and side-by-side, without the user retyping the message per quadrant.

**Acceptance Scenarios**:

1. **Given** a multi-chat session with N quadrants (2 ≤ N ≤ 4), each assigned a model, **When** the user types one message and sends it once, **Then** that message is dispatched in parallel to every quadrant's assigned model.
2. **Given** a parallel dispatch to N models, **When** responses arrive, **Then** each quadrant renders its own model's response independently, side-by-side in the same view, without waiting for every other quadrant to complete.
3. **Given** one model in the parallel dispatch errors or is slower than the others, **When** the remaining models' responses succeed, **Then** those quadrants still render their responses normally rather than being blocked by the slow or failing one.
4. **Given** a user selecting a model for a quadrant, **When** the model list is presented, **Then** only models permitted by the caller's role-based allow-list are selectable (constraint defined in spec 014; not re-specified here).

### Edge Cases

- What happens when a persisted multi-chat session references a thread that was since deleted, or a persona that was since deleted or unshared from the user?
- What happens if a user has the same multi-chat session open in two browser tabs/devices simultaneously and both mutate quadrant/persona assignments?
- What happens when a send fails partway — e.g., the thread is created successfully but message/attachment delivery to it fails? (Should not be treated identically to a full thread-creation failure, but must still not silently drop input.)
- How does a restored session behave if the number of persisted quadrants is outside the current 2–4 bounds (e.g., legacy data, or a bound change)?
- What happens when a starred persona is later deleted or unshared — does it silently disappear from Chat-Home, or is that surfaced?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST durably persist multi-chat session state — quadrant count, per-quadrant persona assignment, and per-quadrant thread association — beyond the browser's in-memory state.
- **FR-002**: On loading Multi-Chat, the system MUST restore the user's most recent persisted session state (quadrant count, persona assignments, thread associations) rather than starting from a blank layout.
- **FR-003**: WHEN a user sends the first message in an empty quadrant, THE SYSTEM SHALL create a thread on demand before sending, and MUST persist the resulting thread association as part of session state immediately, not only in memory.
- **FR-004**: Quadrant count MUST floor at 2: removing a quadrant while at the 2-quadrant minimum MUST clear that quadrant's persona assignment rather than remove the slot.
- **FR-005**: Quadrant count MUST cap at 4: attempts to add beyond 4 quadrants MUST be rejected.
- **FR-006**: WHEN on-demand thread creation fails during a send, THE SYSTEM SHALL surface a user-facing error in the affected quadrant and MUST NOT silently discard the user's typed message or attachments.
- **FR-007**: Following a failed send, the user's typed message text and any attachments MUST remain available for retry without requiring the user to retype or re-attach them.
- **FR-008**: Chat-Home MUST display only the current user's starred/favorite personas.
- **FR-009**: WHEN a user has no starred personas, THE SYSTEM SHALL show an empty state prompting the user to star personas from the main Assistants page.
- **FR-010**: The system MUST let a user author a single message once and dispatch it in parallel to every quadrant's currently assigned model in the active multi-chat session, without requiring the message to be retyped per quadrant (PRD REQ-MULTICHAT-1).
- **FR-011**: The system MUST render each quadrant's response independently and side-by-side in the same view as responses arrive, such that one model's latency or failure does not block another quadrant's response from displaying (PRD REQ-MULTICHAT-1). Which models a caller may assign to a quadrant is constrained by the role-based model allow-list defined in spec 014 (FR-001, FR-003); this spec does not duplicate that constraint.

### Key Entities *(include if feature involves data)*

- **MultiChatSession**: the persisted representation of a user's multi-chat layout — quadrant count (2–4) and, per quadrant, the assigned persona (if any) and associated thread (if any). Currently has no durable backing store; this spec requires one.
- **ChatThreadModel**: existing entity already carrying `multiChatSessionId?`/`multiChatPosition?` fields intended to link a thread back to its multi-chat slot — this spec is the first to actually populate and rely on them.
- **PersonaModel**: referenced by quadrant assignment; Chat-Home filters the user's accessible personas down to those marked as starred/favorite.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of multi-chat sessions in the test suite retain identical quadrant count, persona assignments, and thread associations after a page refresh or re-navigation.
- **SC-002**: 0% of simulated thread-creation failures during send result in permanently lost message text or attachments — all are recoverable in the input for retry.
- **SC-003**: 100% of failed sends produce a visible, user-facing error, replacing today's console-only logging.
- **SC-004**: 100% of Chat-Home loads for users with zero starred personas show the empty-state prompt; 100% of loads for users with one or more starred personas show only starred personas.
- **SC-005**: 100% of test-suite single-message sends across 2–4 quadrants result in that same message being dispatched to all assigned models in parallel, with each quadrant's response rendered independently and side-by-side, unblocked by other quadrants' latency or failure.

## Assumptions

- Persistence is scoped per authenticated user (one durable multi-chat session per user), consistent with how other per-user state (threads, personas) is scoped elsewhere in this codebase; multi-device/multi-tab conflict resolution beyond "last write wins" is not required by this spec.
- The existing per-thread persona-ownership reliance (multi-chat thread creation trusts that the persona was already scoped to the caller via `FindAllPersonaForCurrentUser`) is unchanged by this spec; no new authorization mechanism is introduced here.
- The "starred/favorite persona" concept and its storage already exist elsewhere in the system (surfaced via the main Assistants page); this spec only formalizes Chat-Home's read/filter/empty-state behavior against it, not the starring mechanism itself.
- Lesson Mode and Canvas submission behavior (also under `features/lesson-chat/`, `features/lesson-mode/`) are explicitly out of scope for this spec.
- Which models a user may assign to a quadrant is governed by the role-based model allow-list computation specified in spec 014-model-access-config-management (FR-001, FR-003); this spec covers only the parallel-send and side-by-side-render behavior of Multi-Chat (User Story 4), not model-access authorization.
