# Feature Specification: Chat Web UI

**Feature Branch**: `024-chat-web-ui`

**Created**: 2026-07-29

**Status**: Draft

**Input**: User description: "Create a new spec for the Blazor Chat UI — the web interface for R1's 'Authenticated Enterprise Chat' pilot, which has a fully working backend (specs 002, 014, 004, 006, 017) but no actual screen a user can open. Scope: sign-in/app shell; single-chat screen (create thread, send message, stream response, view history); thread list/switch/rename (new backend capabilities needed — spec 004 has no list-my-threads or rename endpoint today); multi-chat screen (quadrant layout, per-quadrant model assignment, single-message parallel send, side-by-side streaming); changelog/version-alert surface (lower priority). UI-layer spec — describes user-facing behavior, not implementation."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sign in and land on a working chat home (Priority: P1)

A user opens the application, is authenticated (via the enterprise sign-in already in place), and arrives at a chat home screen — not a placeholder page — from which they can start or resume a conversation.

**Why this priority**: Every other story in this spec assumes the user can actually reach a real screen. Today the app's landing page is unbuilt scaffold; nothing else here is reachable until this exists.

**Independent Test**: Sign in as an authenticated user and confirm the landing screen shows the user's identity and a way to start a new conversation, rather than a generic placeholder.

**Acceptance Scenarios**:

1. **Given** an authenticated user, **When** they open the application, **Then** they land on a chat home screen that identifies them and offers to start a new conversation.
2. **Given** an unauthenticated visitor, **When** they open the application, **Then** they are routed into the existing sign-in flow before reaching any chat content.

---

### User Story 2 - Start a conversation and watch the response stream in (Priority: P1)

A user starts a new conversation, types a message, sends it, and watches the assistant's reply appear token-by-token rather than all at once, then can continue the conversation with follow-up messages.

**Why this priority**: This is the core value of the whole pilot — everything else (history, multi-chat, changelog) is secondary to a user being able to actually have a conversation. Without this, the backend chat pipeline has no way to be used.

**Independent Test**: Start a new conversation, send a message, and confirm the reply visibly renders incrementally (not as a single delayed block), and that a follow-up message continues the same conversation with prior context visible.

**Acceptance Scenarios**:

1. **Given** a user with no active conversation, **When** they type a message and send it, **Then** a new conversation is created and the message appears in the transcript immediately.
2. **Given** a message has been sent, **When** the assistant responds, **Then** the response renders incrementally as it arrives, not only after the full response is complete.
3. **Given** an active conversation with prior messages, **When** the user sends another message, **Then** it is added to the same conversation and the assistant's reply reflects the prior context.
4. **Given** a message is rejected by a server-side guard (e.g., a daily limit or a blocked-content signal), **When** the rejection occurs, **Then** the user sees a clear, specific message explaining why, not a generic error.
5. **Given** an unexpected failure occurs while sending, **When** it happens, **Then** the user sees a generic, non-alarming error and can retry, with no internal error detail exposed.

---

### User Story 3 - See, switch between, and rename past conversations (Priority: P2)

A user with multiple prior conversations opens a list of their own conversations, switches into one to continue it, and renames a conversation to something more memorable than its default label.

**Why this priority**: A pilot user who can only ever see their most recent conversation will lose track of earlier work; this is the next most valuable capability after the core send/receive loop, but the pilot is still usable without it (Story 2 alone is a viable MVP).

**Independent Test**: Create two or more conversations, confirm both appear in a list scoped to the current user, switch into an older one and confirm its prior messages render, then rename it and confirm the new name persists across a page reload.

**Acceptance Scenarios**:

1. **Given** a user has started one or more conversations, **When** they open the conversation list, **Then** they see only their own conversations, each distinguishable from the others.
2. **Given** a list of conversations, **When** the user selects one, **Then** its full prior message history loads and the user can continue it.
3. **Given** an existing conversation, **When** the user renames it, **Then** the new name is shown in the list and persists after the page is reloaded.
4. **Given** a user has no conversations yet, **When** they open the list, **Then** they see an empty state that invites them to start one, not an error.

---

### User Story 4 - Compare multiple models on the same message side-by-side (Priority: P2)

A user opens a multi-way comparison view, assigns a different model to each of several panes, types one message, sends it once, and watches each pane's response stream in independently so they can compare answers directly.

**Why this priority**: This is a distinct, higher-value capability for users evaluating models, but it is additive on top of Story 2's core send/receive loop rather than a prerequisite for it — the pilot delivers value with single-chat alone.

**Independent Test**: Open the comparison view, assign models to at least two panes, send one message, and confirm each pane renders its own response independently, with a slower or failing pane never blocking the others from completing.

**Acceptance Scenarios**:

1. **Given** the comparison view is open with its default panes, **When** the user assigns a model to a pane, **Then** that assignment is reflected in the pane and persists across a page reload.
2. **Given** two or more panes each have a model assigned, **When** the user types one message and sends it, **Then** the same message is dispatched to every assigned pane without needing to be retyped.
3. **Given** responses are streaming into multiple panes, **When** one pane's response is slower than another's, **Then** the faster pane's response completes and displays without waiting for the slower one.
4. **Given** one pane's send fails, **When** the failure occurs, **Then** that pane shows a clear error while the other panes continue to stream normally.
5. **Given** a user is at the minimum number of panes, **When** they remove a pane, **Then** its model assignment is cleared but the pane itself remains (the layout never drops below the minimum); **Given** a user is at the maximum, **When** they try to add another, **Then** the request is refused with a clear reason.

---

### User Story 5 - See what's new and be notified of updates (Priority: P3)

A user opens a changelog view to see what has changed recently, and separately notices a dismissible notice when a newer version exists that they haven't yet acknowledged.

**Why this priority**: Valuable for keeping pilot users informed, but it carries no risk of blocking or degrading the core chat experience, and there is no user-facing capability here beyond convenience — hence lowest priority.

**Independent Test**: Open the changelog view and confirm recent entries render; separately, simulate a newer version than the user's last acknowledgment and confirm a dismissible notice appears, then confirm dismissing it prevents the same notice from reappearing immediately.

**Acceptance Scenarios**:

1. **Given** changelog content exists, **When** the user opens the changelog view, **Then** entries render newest-first.
2. **Given** no changelog content is available, **When** the user opens the changelog view, **Then** they see a defined empty state, not an error.
3. **Given** a newer version exists than the user has acknowledged, **When** they use the application, **Then** a dismissible notice appears.
4. **Given** the notice is shown, **When** the user dismisses it, **Then** it does not reappear on their next visit within the acknowledgment window already defined by the underlying system.

### Edge Cases

- What happens if a user's connection drops mid-stream? The partially-received response should remain visible (not disappear), and the user should be able to tell the response was interrupted rather than assuming it finished normally.
- What happens if a user opens the same conversation in two browser tabs at once? Both should be able to view it; this spec does not require real-time sync of one tab's new messages into the other (a manual refresh is an acceptable way to see the other tab's updates).
- What happens when a user renames a conversation to an empty or whitespace-only name? The rename should be rejected with a clear message, leaving the previous name in place.
- What happens when a user navigates directly to a conversation ID that isn't theirs (or doesn't exist)? They should see a clear "not found" state, never another user's content.
- What happens when a multi-chat pane has no model assigned yet and the user sends a message? That pane should visibly indicate it has nothing to send to, rather than silently doing nothing.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST route an unauthenticated visitor into the existing sign-in flow before showing any chat content, and MUST show an authenticated user a chat home screen identifying them and offering to start a new conversation.
- **FR-002**: Users MUST be able to start a new conversation, send a message into it, and see the assistant's response render incrementally as it is received, not only after it completes.
- **FR-003**: Users MUST be able to continue an existing conversation with a follow-up message that reflects the conversation's prior context.
- **FR-004**: WHEN a message is rejected by a server-side guard (rate limit, content policy, or similar), THE SYSTEM MUST show the user a clear, specific reason; WHEN an unexpected failure occurs, THE SYSTEM MUST show a generic error with no internal detail exposed, in both cases leaving the user's typed input recoverable for retry.
- **FR-005**: The system MUST provide a way for a user to list their own conversations and MUST NOT include any other user's conversations in that list.
- **FR-006**: Users MUST be able to select a conversation from their list and continue it, with its prior messages rendered before they send anything new.
- **FR-007**: Users MUST be able to rename one of their own conversations; the new name MUST persist and MUST be rejected if empty or whitespace-only, leaving the prior name in effect.
- **FR-008**: The system MUST provide a multi-pane comparison view where each pane can be independently assigned a model, honoring the same minimum/maximum pane-count rules already enforced by the underlying session (removing a pane at the minimum clears its assignment rather than removing the pane; adding beyond the maximum is refused).
- **FR-009**: WHEN a user sends one message from the comparison view, THE SYSTEM MUST dispatch it to every pane with an assigned model and MUST render each pane's response independently as it arrives, such that one pane's latency or failure never delays or blocks another pane's response from displaying.
- **FR-010**: The system MUST provide a changelog view showing available entries newest-first, and MUST show a defined empty state (not an error) when no entries are available.
- **FR-011**: WHEN a newer changelog version exists than the user has acknowledged, THE SYSTEM MUST show a dismissible notice; dismissing it MUST be recorded so the same notice does not reappear immediately on the next visit.
- **FR-012**: A conversation's message transcript, once partially received, MUST remain visible to the user even if the connection is interrupted before the response completes, and the interruption MUST be visually distinguishable from a normally-completed response.
- **FR-013**: Navigating to a conversation the current user does not own (or that does not exist) MUST show a clear not-found state and MUST NOT reveal any content belonging to another user.

### Key Entities *(include if feature involves data)*

- **Conversation summary**: the list-facing representation of a conversation — identifier, display name (renamable), and enough detail (e.g., last-updated) to distinguish it in a list. Backed by the existing conversation/thread record; this spec adds the *listing* and *renaming* capability on top of the existing single-conversation create/read capability, which do not exist yet.
- **Streaming message view-state**: the client-side, in-progress representation of a response as it arrives incrementally, including whether it completed normally or was interrupted.
- **Comparison session**: the multi-pane layout a user sees — pane count, per-pane model assignment — backed by the existing multi-conversation session capability.
- **Changelog entry / acknowledgment**: the versioned content and the user's dismissal record, backed by the existing changelog and acknowledgment capability.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of authenticated users landing on the application see a working chat home screen (not a placeholder), across a repeated test run.
- **SC-002**: A user can send a message and see the first part of the assistant's response begin rendering in under 3 seconds under normal conditions, without waiting for the full response.
- **SC-003**: 100% of a test user's own conversations appear in their conversation list, and 0% of another user's conversations ever appear in it.
- **SC-004**: 100% of conversation renames with a non-empty name persist across a page reload; 100% of empty/whitespace rename attempts are rejected with the prior name intact.
- **SC-005**: In a comparison session with 2-4 panes each assigned a model, 100% of test runs show every pane's response rendering independently, with a deliberately slowed or failed pane never delaying another pane's completed response.
- **SC-006**: 100% of simulated connection interruptions mid-response leave the partial response visible and marked as interrupted, rather than disappearing or appearing complete.
- **SC-007**: 100% of attempts to open another user's conversation (by guessing or reusing an identifier) result in a not-found state, never that user's content.

## Assumptions

- Sign-in itself (the Entra/dev-authentication mechanism) is out of scope — this spec only requires that unauthenticated visitors are routed into whatever sign-in flow already exists and that an authenticated identity is available to the chat home screen.
- "List my conversations" and "rename a conversation" are new capabilities this spec requires of the underlying conversation system — no existing spec provides them; a conversation currently can only be created and fetched by an id already known to the caller.
- Real-time synchronization of a conversation across multiple simultaneously-open tabs/devices is out of scope (per Edge Cases, a manual refresh is an acceptable way to observe another tab's changes) — consistent with the equivalent assumption already made for the multi-pane comparison session.
- The comparison view's pane minimum/maximum and per-pane model-assignment rules are inherited from the existing comparison-session capability, not redefined here.
- Attachments, multi-model persona configuration, and any other capability explicitly deferred by the specs this UI consumes (chat pipeline, comparison session, changelog) remain deferred here too — this spec adds a visible surface for what those systems already support, not new backend capability beyond the two named in FR-005/FR-007 (list, rename).
- Accessibility, internationalization, and mobile-specific layout are not addressed as distinct requirements in this pass; the pilot targets desktop browser usage by employees, consistent with the "limited production pilot, employees-only" scope already established for this release.
