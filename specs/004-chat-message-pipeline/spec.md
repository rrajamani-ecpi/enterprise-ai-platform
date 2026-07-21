# Feature Specification: Chat Message Pipeline, Tool Safety & Model Access Control

**Feature Branch**: `004-chat-message-pipeline`

**Created**: 2026-07-20

**Status**: Draft

**Input**: Derived from SSD_Document.md §3.2 (Chat Core) — reframed from "as-is" discovery findings into target requirements, scoped to the message-limit, prompt-assembly, PII-redaction, tool-orchestration, model-access-control, artifact-security, sharing, and persona-invocation material (document ingestion/retrieval is out of scope — covered separately). Source facts: `messageLimitPreflight` runs before any thread/message record is touched and caps fail open on config/counter read failure; client-supplied `dataProducts` are always discarded server-side in favor of the thread's stored values (anti-IDOR); HTML artifacts render in a real sandboxed iframe but React artifacts execute via `react-live` in the same JS realm as the app despite design docs claiming otherwise; shared-thread access checks only "is the caller logged in" (not "is the caller the intended recipient") and shares never expire; tool-specific failure handling is inconsistent (Calculator/Weather let exceptions propagate raw while Map/DataProduct/CSV/ImageGen return a structured `{success:false}` payload); model-access enforcement silently substitutes the default model when a requested model isn't in the caller's role allow-list. Extended per `docs/PRODUCT_REQUIREMENTS_DOCUMENT.md` §4.2 (Conversational Chat (core)) and §4.16 (Artifacts), the primary forward-looking sources, and `docs/prd-decomposition-plan.md`'s routing of §4.2 and §4.16 here, which flags context-window compression (REQ-CHAT-5) and max-thread-size handling (REQ-CHAT-10) as not yet covered by this spec, and separately notes that this spec's existing artifact coverage (User Story 2) addresses only the React sandboxing bug, not the base artifact-panel capability (REQ-ARTIFACT-1) itself. Source facts confirming the gap: `ChatThreadModel` defines `MAX_CHAT_THREAD_SIZE = 2,000,000` and `ChatMessageModelV2`'s `MessageMetadata` carries an optional `compressionEvent` (original/compressed token counts, messages compressed), but neither has any described enforcement/trigger logic in the as-is discovery — the schema anticipates both capabilities without the business logic behind them ever being specified. The remaining REQ-CHAT items (1–4, 6–9) are cross-checked against this spec and its siblings in Assumptions below rather than re-specified. Also closes a gap `docs/prd-decomposition-plan.md` flagged during the Personas merge pass: **REQ-PERSONA-2** ("start a chat directly from a persona, applying its model, instructions, tools, and data products") had no home in spec 009 (CRUD/authorization) or spec 010 (builder/preview) — it is a chat-invocation/usage-flow behavior specified here as User Story 10, cross-referencing spec 009 for the `PersonaModel` schema itself rather than redefining it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Blocked or invalid messages leave no trace and no legacy thread is mutated (Priority: P1)

A user sends a chat message that should be blocked (over a configured cap, or targeting a read-only legacy thread). The system must reject it before creating or touching any persistence record, and must never silently fail closed due to an infrastructure blip in the limit-checking path itself.

**Why this priority**: This is the gate every other requirement in this spec sits behind. If a blocked message still creates a thread/message record, or a legacy (`v1`/`v2`) thread can silently be continued, or a transient config/counter read failure blocks a legitimate user, the rest of the pipeline (prompt assembly, PII redaction, tool orchestration, model access) is being run on a broken foundation. Nothing else here matters until this ordering and fail-open guarantee holds.

**Independent Test**: Send a message that exceeds the daily cap and confirm no thread/message document is created; send a message to a `v1`/`v2` thread and confirm a 409 with no mutation; simulate a config/counter read failure and confirm the message is allowed through rather than blocked.

**Acceptance Scenarios**:

1. **Given** the daily message cap is enabled and already exceeded for a user, **When** they send another message, **Then** the request is rejected with 402 `DAILY_MESSAGE_LIMIT_EXCEEDED` (including `resetsAt`) and no thread or message record is created or modified.
2. **Given** a thread with `version !== "v3"`, **When** the user attempts to continue it, **Then** the request is rejected with 409 `THREAD_READ_ONLY` before any record is touched.
3. **Given** the message-limit config or counter store is unreachable, **When** a user sends a message, **Then** the message is allowed through (fail open) rather than blocked.
4. **Given** a chat request whose body includes a `dataProducts` array different from the thread's stored data products, **When** the request is processed, **Then** the server discards the client-supplied value and uses only the thread's stored data products for prompt assembly.

---

### User Story 2 - Artifacts execute in genuine, consistent isolation regardless of type (Priority: P1)

A user asks a persona to generate an interactive artifact. Today, HTML artifacts render in a properly sandboxed iframe (`allow-scripts` only, no `allow-same-origin`), but React artifacts execute via `react-live` in the same JS realm as the host application — meaning generated/model-produced React code can reach application globals, cookies, and DOM outside any sandbox boundary, contradicting design docs that describe a sandbox.

**Why this priority**: This is a live code-execution security gap, not a cosmetic inconsistency — a malicious or compromised model response containing React artifact code currently runs with the same privileges as the application itself. It is ranked alongside the sharing fix below as the most severe issue in this domain and must not ship unresolved.

**Independent Test**: Generate a React artifact containing code that attempts to read `document.cookie`, access an application-scoped global, or reach outside its render boundary, and confirm it cannot — using the same test harness already used to validate HTML iframe isolation.

**Acceptance Scenarios**:

1. **Given** a generated HTML artifact, **When** it renders, **Then** it continues to run in a sandboxed iframe with `allow-scripts` only (no `allow-same-origin`).
2. **Given** a generated React artifact, **When** it renders, **Then** it executes in isolation equivalent to the HTML sandbox — it cannot access the host application's JS realm, globals, cookies, or DOM outside its own render boundary.
3. **Given** a generated SVG artifact, **When** it renders, **Then** it continues to be sanitized via the DOMPurify SVG profile, forbidden-tag/attribute stripping, and a URI-scheme allow-list (http/https/mailto/tel/relative only).
4. **Given** internal documentation describing artifact sandboxing, **When** this feature ships, **Then** the documentation accurately reflects the actual isolation level for every artifact type (no type is documented as sandboxed unless it demonstrably is).

---

### User Story 3 - Shared threads are only accessible to their intended recipient and can expire (Priority: P1)

A user shares a chat thread, producing a `shareId`. Today, `GetSharedThread` checks only that the caller is authenticated — not that they are the person the thread was shared with — and shares never expire, so any authenticated holder of a `shareId` (guessed, leaked, or previously revoked) can view or clone it indefinitely.

**Why this priority**: This is an authorization bypass, not a UX gap — it allows access to arbitrary other users' shared conversations. It carries the same severity as the artifact isolation issue above and is ranked P1 alongside it.

**Independent Test**: Share a thread with a specific recipient, then attempt to access the resulting `shareId` as a different authenticated user and confirm it is rejected; separately, access a share after its expiry window has passed and confirm it is rejected.

**Acceptance Scenarios**:

1. **Given** a thread shared with a specific intended recipient, **When** a different authenticated user requests it by `shareId`, **Then** access is rejected.
2. **Given** a thread shared with an intended recipient, **When** that recipient requests it by `shareId` before expiry, **Then** access succeeds (view and clone both function as today).
3. **Given** a share whose expiry window has elapsed, **When** any caller (including the original intended recipient) requests it, **Then** access is rejected.
4. **Given** a thread owner revokes or re-shares a thread, **When** the previous `shareId` is used afterward, **Then** access is rejected.

---

### User Story 4 - All tools fail the same way (Priority: P2)

A tool invocation (Calculator, Weather, Map, DataProduct, CSV export, ImageGen) throws an error during execution. Today, Calculator and Weather let the raw exception propagate, while Map/DataProduct/CSV/ImageGen catch it and return a structured `{success:false}` payload back into the model's tool-result turn.

**Why this priority**: This inconsistency risks destabilizing the stream (an uncaught exception from one tool behaves differently than a handled one from another) and produces unpredictable, tool-dependent failure messaging to the model and end user. It is a real defect but lower severity than the artifact and sharing gaps above, since it does not expose cross-user data or arbitrary code execution.

**Independent Test**: Force a failure in each of the six tools (e.g., malformed input, unreachable dependency) and confirm every one of them returns the same structured `{success:false}` shape into the stream, with no raw exception escaping any tool.

**Acceptance Scenarios**:

1. **Given** a Calculator or Weather tool call that would previously throw, **When** the underlying operation fails, **Then** the tool returns a structured `{success:false, error}` payload instead of letting the exception propagate.
2. **Given** any of the six tools fails, **When** the failure is surfaced to the model, **Then** the payload shape is identical across all tools.
3. **Given** a tool failure of any kind, **When** it is logged, **Then** the outer stream continues uninterrupted (a single tool failure never aborts the whole response).

---

### User Story 5 - Model access stays a non-bypassable, role-based gate (Priority: P3)

A user's persona or request specifies a model. The system must resolve the caller's effective model allow-list (env override → Cosmos config → defaults → `"*"`) and enforce it regardless of what the client claims is available, substituting the configured default model whenever the requested model falls outside the caller's role allow-list.

**Why this priority**: This behavior is already correct today and is the one non-bypassable gate in the extension/model system (all client-side visibility logic is advisory only). It is included here as a regression-protection baseline rather than a bug fix, so it is ranked lowest — there is no known gap to close, only a guarantee to keep verified as the pipeline evolves.

**Independent Test**: Request a model outside the caller's role allow-list directly via the API (bypassing UI model pickers) and confirm the default model is used instead, never the requested one.

**Acceptance Scenarios**:

1. **Given** a caller whose role allow-list does not include the requested model, **When** they send a chat request specifying that model, **Then** the system substitutes the role's/system's configured default model and proceeds.
2. **Given** a model that `requiresAdvancedModelAccess`, **When** a caller without that flag requests it, **Then** the same substitution applies regardless of role allow-list contents.
3. **Given** an image attachment exceeding the provider's size limit (e.g., >5MB for Anthropic), **When** it is submitted, **Then** the request is rejected pre-flight with 400 `IMAGE_TOO_LARGE` rather than failing at the provider.
4. **Given** a CSV export containing a value starting with `= + - @` or a control character, **When** it is exported, **Then** the value is quote-prefixed to defeat spreadsheet formula/DDE injection.
5. **Given** an unhandled exception anywhere in the chat entry point or stream execution, **When** it occurs, **Then** the client receives a generic 500 with no internal error detail leaked.

---

### User Story 6 - Long threads stay within the model's context window via compression (Priority: P2)

A conversation grows long enough that its accumulated token count approaches the active model's context window. Today `ChatMessageModelV2`'s `MessageMetadata` already models a `compressionEvent` field (original/compressed token counts, messages compressed), but no described logic in the current system actually triggers compression, meaning long threads risk exceeding the model's context window with no defined fallback.

**Why this priority**: Per PRD §4.2 (REQ-CHAT-5), this is core conversational-chat capability, not an edge case — an uncompressed thread that exceeds the context window either fails the model call outright or silently truncates in an unspecified way. It ranks below the P1 security/data-integrity stories above but is a functional gap serious enough to block long-running conversations.

**Independent Test**: Build a thread whose accumulated token count approaches the active model's configured `contextWindowSize`, send another message, and confirm older messages are compressed into a retrievable summary before prompt assembly, with the resulting prompt staying within the context window and a `compressionEvent` recorded.

**Acceptance Scenarios**:

1. **Given** a thread's accumulated token count approaches the active model's configured `contextWindowSize`, **When** the user sends another message, **Then** the system compresses/summarizes the oldest portion of the thread's history before assembling the prompt, so the effective prompt stays within the context window.
2. **Given** a compression event occurs, **When** it completes, **Then** `MessageMetadata.compressionEvent` records the original token count, the compressed token count, and the number of messages compressed.
3. **Given** a thread that has been compressed, **When** the user views the thread's history, **Then** a retrievable summary of the compressed messages remains accessible rather than being discarded outright.
4. **Given** a thread whose token count is well within the context window, **When** a new message is sent, **Then** no compression occurs and the full history is used unchanged.

---

### User Story 7 - Threads approaching the maximum stored size are handled deterministically (Priority: P2)

A thread's persisted size grows toward `MAX_CHAT_THREAD_SIZE` (2,000,000). Today this constant exists on `ChatThreadModel` but no described logic enforces it, meaning a thread that crosses this boundary risks an unhandled storage-layer failure rather than a defined, user-facing outcome.

**Why this priority**: Per PRD §4.2 (REQ-CHAT-10), an oversize thread must fail safely and predictably, the same way Story 1 requires predictable, non-mutating behavior for blocked messages. Ranked alongside Story 6 since both are schema-modeled-but-unenforced gaps in the same domain.

**Independent Test**: Grow a thread's persisted size to at/near `MAX_CHAT_THREAD_SIZE`, send an additional message that would push it over, and confirm the system follows a defined, deterministic path (typed rejection, or compression per Story 6) rather than an unhandled storage error.

**Acceptance Scenarios**:

1. **Given** a thread's persisted size is at or approaching `MAX_CHAT_THREAD_SIZE`, **When** a new message would push it over the limit, **Then** the system follows a defined, deterministic handling path (e.g., a typed rejection, or compression per Story 6) rather than surfacing an unhandled storage-layer failure.
2. **Given** a thread at `MAX_CHAT_THREAD_SIZE` that cannot accept a new message under the enforced handling path, **When** the user attempts to continue it, **Then** the user receives a distinct, typed error rather than a silent failure or generic 500.
3. **Given** a thread well under `MAX_CHAT_THREAD_SIZE`, **When** messages are sent, **Then** no oversize handling is triggered.

---

### User Story 8 - Conversation and per-message feedback is captured at a configurable sample rate (Priority: P3)

A user finishes a conversation, or reacts to a specific assistant message, and the system prompts for optional feedback: a 1-5 conversation rating, or a per-message thumbs up/down. Prompting is governed by a configurable sampling rate so not every conversation or message is prompted. Submitted feedback is validated, ownership-checked, and forwarded exactly as spec 017 already defines for feedback submissions — this story covers only the rating/thumb capture and sampling layer PRD §4.2 (REQ-CHAT-9) adds on top of that.

**Why this priority**: The underlying proxy/ownership/non-persistence mechanics (spec 017, User Story 3, FR-008–FR-010) are already specified and correct; what's missing is the conversation-rating-plus-per-message-thumb capture shape and the configurable sampling rate that governs when it's offered. Ranked P3 as a capture/UX layer, not a security or data-integrity gap.

**Independent Test**: Configure a sampling rate, exercise the chat surface across multiple conversations and messages, and confirm the conversation-rating (1-5) and per-message thumb prompts appear at approximately the configured rate; confirm a submitted rating or thumb is forwarded via the same ownership-checked path spec 017 defines.

**Acceptance Scenarios**:

1. **Given** a configurable sampling rate, **When** a user completes a conversation, **Then** the conversation-rating (1-5) prompt is shown only at approximately the configured sampling rate, not on every conversation.
2. **Given** an assistant message, **When** the user submits a thumbs up/down on it, **Then** it is captured as feedback tied to that specific message.
3. **Given** a conversation rating or per-message thumb is submitted, **When** it is processed, **Then** it is validated, ownership-checked against the caller's own thread, and forwarded to the feedback service via the same proxy path defined in spec 017 (FR-008–FR-010), without re-implementing that validation/forwarding logic here.

---

### User Story 9 - Assistant-generated artifacts render in a dedicated, persisted panel separate from chat (Priority: P2)

A user asks a persona to produce something interactive — runnable code, a rendered UI component — and expects it to appear in its own workspace alongside the conversation, not as an inline chat bubble, and to still be there (in the same state) if they navigate away and come back. Today this spec only addresses whether React artifacts are safely isolated when they render (Story 2); it does not specify the underlying capability of generating an artifact, displaying it in a dedicated panel, or persisting its state separately from the chat transcript.

**Why this priority**: Per PRD §4.16 (REQ-ARTIFACT-1), this is the base artifact capability that Story 2's sandboxing guarantee sits on top of — without it, there is no specified panel or state store for that isolation to apply to. Ranked P2: it is a foundational capability gap, not a security defect, so it ranks below the P1 security/data-integrity stories but above the lower-priority capture/regression stories.

**Independent Test**: Prompt a persona to generate an artifact and confirm it renders in a dedicated panel distinct from the message list; confirm the panel's content/view state is retrievable from its own store after navigating away and returning, independent of `ChatMessageModelV2`.

**Acceptance Scenarios**:

1. **Given** a persona response that includes a generatable artifact (code or UI component), **When** the response completes, **Then** the artifact is displayed in a dedicated panel separate from the chat message transcript, not inline as a chat bubble.
2. **Given** an artifact panel open for a thread, **When** the user sends another chat message or navigates within the thread, **Then** the artifact's content and view state persist in their own state store, distinct from the chat message store.
3. **Given** a thread containing one or more previously generated artifacts, **When** the user reopens that thread, **Then** the artifact state is retrievable from its dedicated store without depending on the chat transcript.
4. **Given** an artifact of type `html`, `react`, or `svg` rendering in the dedicated panel, **When** it executes, **Then** the isolation/sanitization guarantees already defined in User Story 2 (FR-013–FR-016) apply unchanged — this story does not restate or alter those security requirements.

---

### User Story 10 - Starting a chat from a persona applies its full configuration (Priority: P2)

A user selects a persona and starts a new chat directly from it. Today, `ChatThreadModel` carries `personaId`, `personaMessage`, and `model` fields, but nothing in this spec specifies that starting a chat from a persona must actually copy that persona's current model, instructions, enabled tools, and attached data products into the new thread — the base persona-invocation flow was unspecified in any existing spec.

**Why this priority**: Per PRD §4.2 (REQ-PERSONA-2), this is core usage-flow behavior — without it, a persona is just a saved record with no defined effect on the chat it produces. Ranked P2: it is a foundational capability gap like Stories 6/7/9, not a security or data-integrity defect, so it ranks below the P1 stories above.

**Independent Test**: Create a persona with a specific model, persona message, one or more enabled extensions, and an attached data product; start a new chat from it; confirm the resulting thread's initial model, personaMessage, enabled extensions, and dataProducts match the persona's configuration at the moment the chat was started.

**Acceptance Scenarios**:

1. **Given** a persona with a configured model, persona message, enabled extensions, and attached data products, **When** a user starts a new chat from that persona, **Then** the new thread is created with that model, that persona message, those enabled extensions, and those data products.
2. **Given** a persona with an optional `startingMessage`, **When** a chat is started from it, **Then** the new thread's initial message state is seeded from that `startingMessage`.
3. **Given** a persona whose configured model falls outside the current caller's role allow-list, **When** a chat is started from it, **Then** the same model-substitution gate (FR-020/FR-021) applies — starting a chat from a persona MUST NOT bypass model-access enforcement.
4. **Given** a thread already created from a persona, **When** that source persona is later edited (e.g., its persona message changes), **Then** the existing thread's configuration is unaffected — persona configuration is captured as a snapshot at thread-creation time, not a live reference.

### Edge Cases

- Should the silent default-model substitution (User Story 5) become user-visible (e.g., a notice that the requested model was unavailable and a substitute was used), or should it remain silent as today? This spec keeps the substitution behavior itself unchanged and flags the visibility question as open rather than mandating a UI change.
- What happens when a share is accessed concurrently with the owner revoking/expiring it — is there a race where an in-flight clone completes after expiry takes effect?
- How should PII redaction interact with a message that is blocked by a cap — does redaction ever run on text that is never actually sent (it must not, since redaction is scoped only to user-authored text actually sent to the model)?
- What is the correct behavior when a tool times out (30s default, up to 150s override) versus when it throws — do both now funnel into the same structured failure shape from Story 4, or is "timed out" treated as a distinct, still-consistent case?
- How does artifact isolation for React interact with legitimate use cases that currently rely on same-realm access (if any) — does closing the isolation gap break any existing artifact functionality that assumed shared-realm behavior?
- What happens when a recipient's identity changes (e.g., email change) between share creation and access — does recipient-matching need to tolerate identity drift?
- What happens when a thread simultaneously approaches both `MAX_CHAT_THREAD_SIZE` (Story 7) and the model's context window (Story 6) — does compression run first and potentially avert the size cap, or are the two checks independent?
- Does a compression event (Story 6) count toward or reset a thread's progress toward `MAX_CHAT_THREAD_SIZE` (Story 7), given compression reduces token count but not necessarily persisted message count?
- What is shown to the user when a compressed summary (Story 6) itself would need to be compressed again on a subsequent long-running thread — is recursive compression supported, or is there a hard ceiling?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST run message-limit preflight checks before creating or modifying any thread or message record, so that a blocked message leaves no persistence trace.
- **FR-002**: The system MUST reject continuation of any thread whose `version !== "v3"` with 409 `THREAD_READ_ONLY`, before touching any record.
- **FR-003**: The system MUST enforce an independently-toggled per-message character cap (default 4000) when enabled, rejecting over-cap messages with 400 `MESSAGE_TOO_LONG`.
- **FR-004**: The system MUST enforce an independently-toggled daily message cap (default 100, reset at America/New_York midnight) when enabled, rejecting over-cap messages with 402 `DAILY_MESSAGE_LIMIT_EXCEEDED` including `resetsAt`.
- **FR-005**: WHEN the message-limit config or counter store cannot be read, THE SYSTEM MUST fail open (allow the message) rather than block it.
- **FR-006**: The system MUST discard any client-supplied `dataProducts` in the chat request body and build the data-product context block solely from the thread's server-stored data products.
- **FR-007**: The system MUST assemble the model prompt from the base prompt, user name, timezone-aware date/time, persona system prompt, tool-usage instructions (when enabled), and the thread-derived data-product context block (when applicable) — using only server-authoritative inputs.
- **FR-008**: PII redaction MUST be on by default and MUST apply only to user-authored text sent to the model — never to persisted history, assistant output, or documents.
- **FR-009**: Tool execution MUST run under `streamText` with `maxRetries:5` and per-tool timeouts (30s default, overridable up to 150s), returning a "timed out" result to the model rather than hanging the stream.
- **FR-010**: The system MUST send a keepalive ping (approximately every 15 seconds) during long-running streams to prevent idle-timeout termination.
- **FR-011**: Every tool (Calculator, Weather, Map, DataProduct, CSV, ImageGen, and any future tool) MUST catch its own execution failures and return a consistent structured `{success:false, error}` payload — no tool may let a raw exception propagate to the stream.
- **FR-012**: A single tool failure MUST NOT abort or crash the overall chat stream; the model must receive the structured failure and may continue.
- **FR-013**: HTML artifacts MUST render in a sandboxed iframe (`allow-scripts` only, no `allow-same-origin`).
- **FR-014**: React artifacts MUST execute in isolation equivalent to the HTML artifact sandbox — they MUST NOT run in the same JS realm as the host application, and MUST NOT be able to access application globals, cookies, or DOM outside their own render boundary.
- **FR-015**: SVG artifacts MUST be sanitized via the DOMPurify SVG profile, forbidden tag/attribute stripping, and a URI-scheme allow-list (http/https/mailto/tel/relative only).
- **FR-016**: Documentation describing artifact sandboxing MUST accurately reflect the actual isolation level implemented for each artifact type.
- **FR-017**: Shared-thread access MUST verify that the requesting caller is the share's intended recipient, not merely that they hold any valid authenticated session.
- **FR-018**: Shares MUST have an expiry; access to an expired share MUST be rejected regardless of caller identity.
- **FR-019**: Revoking or re-sharing a thread MUST invalidate any previously issued `shareId` for that thread.
- **FR-020**: Model access MUST be resolved via role → allow-list resolution (env override → Cosmos config → defaults → `"*"`) and enforced server-side as the non-bypassable gate, independent of any client-side extension/model visibility logic.
- **FR-021**: WHEN a requested model is outside the caller's resolved allow-list or requires `advancedModelAccess` the caller lacks, THE SYSTEM MUST substitute the configured default model rather than honoring the request.
- **FR-022**: Image attachments exceeding the target provider's size limit (e.g., >5MB for Anthropic) MUST be rejected pre-flight with 400 `IMAGE_TOO_LARGE`.
- **FR-023**: CSV export values starting with `= + - @` or a control character MUST be quote-prefixed to defeat spreadsheet formula/DDE injection.
- **FR-024**: Any unhandled exception in the chat entry point or stream execution MUST result in a generic 500 response that never leaks internal error detail to the client.
- **FR-025**: WHEN a thread's accumulated token count approaches the active model's configured `contextWindowSize`, THE SYSTEM MUST compress/summarize the oldest portion of the thread's message history before assembling the prompt, keeping the effective prompt within the model's context window.
- **FR-026**: Compression events MUST be recorded on `MessageMetadata.compressionEvent`, capturing the original token count, the compressed token count, and the number of messages compressed.
- **FR-027**: A compressed thread MUST retain a retrievable summary of the compressed messages rather than discarding the compressed content outright.
- **FR-028**: The system MUST enforce a defined, deterministic behavior when a thread's persisted size reaches or would exceed `MAX_CHAT_THREAD_SIZE` (2,000,000), rather than allowing an unhandled storage-layer failure.
- **FR-029**: WHEN a thread at or near `MAX_CHAT_THREAD_SIZE` cannot accept a new message under the enforced handling path, THE SYSTEM MUST reject the request with a distinct, typed error rather than a generic 500.
- **FR-030**: The system MUST support capturing an optional conversation-level rating (1-5) and optional per-message thumbs up/down feedback.
- **FR-031**: Feedback prompting (conversation rating and per-message thumb) MUST be governed by a configurable sampling rate, so not every conversation or message is prompted.
- **FR-032**: Conversation ratings and per-message thumbs MUST be forwarded through the same ownership-checked, non-persisted proxy path defined in spec 017 (FR-008–FR-010); this spec does not re-specify that validation/forwarding behavior.
- **FR-033**: The system MUST support generating an interactive artifact (runnable/rendered code or a UI component) from persona/model output.
- **FR-034**: Generated artifacts MUST display in a dedicated panel, visually and structurally separate from the chat message transcript.
- **FR-035**: Artifact content and panel view state MUST be persisted in a state store distinct from `ChatMessageModelV2`, and MUST remain retrievable independent of the chat message history. Isolation and sanitization guarantees for artifact execution remain as specified by FR-013–FR-016 (User Story 2) and are not altered by this requirement.
- **FR-036**: WHEN a user starts a chat from a persona, THE SYSTEM MUST initialize the new thread's model, persona message, enabled extensions, and attached data products from that persona's configuration at the moment of creation (per `PersonaModel`, schema owned by spec 009).
- **FR-037**: WHEN a persona defines a `startingMessage`, THE SYSTEM MUST use it to seed the new thread's initial message state.
- **FR-038**: A persona's configured model MUST still be resolved through the model-access gate (FR-020/FR-021) when starting a chat from it — persona invocation MUST NOT bypass role-based model substitution.
- **FR-039**: A thread's persona-derived configuration (model, persona message, extensions, data products) MUST be captured as a snapshot at creation time; subsequent edits to the source persona MUST NOT retroactively alter already-created threads.

### Key Entities *(include if feature involves data)*

- **ChatThreadModel**: owned chat thread; `version` (`v1`/`v2`/`v3`, only `v3` writable), stored `dataProducts`, sharing fields (`isShared`, `shareId`, `sharedBy`, `sharedAt`, `clonedFromShareId`). Extended by this spec to require a recorded intended recipient and an expiry for any active share, and (Story 7) to enforce a deterministic outcome once persisted size reaches `MAX_CHAT_THREAD_SIZE` (2,000,000).
- **ChatMessageModelV2**: Cosmos document holding `messages: AcceleratorUIMessage[]`; its creation is gated entirely by the preflight checks in Story 1. Extended by this spec (Story 6) to require `MessageMetadata.compressionEvent` be populated whenever compression runs, rather than remaining an unused schema field.
- **ToolResult**: the structured outcome of any tool invocation; standardized by this spec to `{success: boolean, data?, error?}` across all tools, replacing today's raw-exception path for Calculator/Weather.
- **Artifact**: a model-generated renderable object of type `html` | `react` | `svg`, displayed in a dedicated panel separate from the chat transcript with its own persisted state store (FR-033–FR-035); each type's isolation/sanitization guarantee is defined explicitly by this spec (FR-013–FR-016).
- **ArtifactPanelState**: the persisted view/content state of a thread's artifact panel, stored separately from `ChatMessageModelV2` so artifact state survives navigation independent of the chat message history (FR-035).
- **SystemModelConfig / ModelConfigDocument**: role allow-lists and per-model flags (`requiresAdvancedModelAccess`, `isEnabled`) that drive the model-substitution gate in FR-020/FR-021.
- **PersonaModel** *(schema owned by spec 009, referenced here only)*: the source configuration (model, `personaMessage`, `extensions`, `dataProducts`, `startingMessage`) that FR-036/FR-037 snapshot into a new `ChatThreadModel` at chat-start time.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of messages blocked by a cap or read-only-thread check result in zero new/modified thread or message records, across the full preflight test corpus.
- **SC-002**: 100% of simulated config/counter read failures result in the message being allowed (fail-open), 0% result in an erroneous block.
- **SC-003**: 100% of chat requests carrying a client-supplied `dataProducts` value different from the thread's stored value proceed using only the stored value (0 IDOR bypasses in the test corpus).
- **SC-004**: 0 instances of a React artifact reaching application-realm globals, cookies, or DOM outside its render boundary, across a penetration-style test suite equivalent to the one used for HTML iframe isolation.
- **SC-005**: 100% of shared-thread access attempts by an authenticated non-recipient are rejected; 100% of access attempts after expiry are rejected, across the sharing test corpus.
- **SC-006**: 100% of forced failures across all six tools (Calculator, Weather, Map, DataProduct, CSV, ImageGen) return the identical structured `{success:false}` shape, with 0 raw exceptions escaping to the stream.
- **SC-007**: 100% of model requests outside a caller's resolved allow-list result in the default model being used instead, with 0 requests reaching an unauthorized or under-gated model.
- **SC-008**: 100% of image uploads over the provider size limit are rejected pre-flight (never reach the provider call).
- **SC-009**: 100% of threads whose token count reaches the configured context-window threshold trigger compression before the next prompt assembly, with the resulting prompt size within the model's context window, across a test corpus of varying model context sizes.
- **SC-010**: 100% of compression events produce a persisted `compressionEvent` record with accurate original/compressed token counts.
- **SC-011**: 100% of simulated attempts to write a message that would push a thread's stored size over `MAX_CHAT_THREAD_SIZE` result in the defined handling path (typed rejection or compression), with 0 unhandled storage errors.
- **SC-012**: Feedback prompts (conversation rating, per-message thumbs) appear within a statistically reasonable tolerance of a configured sampling rate, across a repeated test run.
- **SC-013**: 100% of generated artifacts across a test corpus render in the dedicated artifact panel rather than inline in the chat transcript.
- **SC-014**: 100% of artifact panel state, across a test corpus of navigation/reload scenarios, remains retrievable from its own store after the user navigates away and returns, independent of the chat message store.
- **SC-015**: 100% of chats started from a persona have their initial model, persona message, enabled extensions, and data products match that persona's configuration at creation time, across a test corpus of personas with varying configurations.
- **SC-016**: 100% of chats started from a persona whose configured model falls outside the caller's allow-list are substituted per FR-020/FR-021, with 0 bypasses via the persona-invocation path.

## Assumptions

- Document ingestion, retrieval/RAG, and PDF export are out of scope for this spec (covered separately); this spec covers only the message-limit, prompt-assembly, PII-redaction, tool-orchestration, model-access, artifact-security, and sharing surfaces of Chat Core.
- Introducing a recorded "intended recipient" on a share is a schema addition to `ChatThreadModel`'s sharing fields (today it is only `isShared`/`shareId`/`sharedBy`/`sharedAt`); this spec assumes recipient identity is captured at share-creation time (e.g., an email, consistent with sharing patterns used elsewhere in the codebase for personas/prompts) rather than introducing a new sharing-permission model from scratch.
- The specific mechanism used to achieve genuine React artifact isolation (e.g., moving React artifact rendering into the same sandboxed-iframe approach as HTML, or an equivalent isolated-realm technique) is an implementation decision; this spec only mandates the resulting isolation property, not the technique.
- The default-model substitution behavior in User Story 5 is retained as-is; whether to make the substitution visible to the end user is explicitly called out as an open edge case, not decided by this spec.
- Existing Azure App Service idle-stream behavior (~60s reset) and the 15-second keepalive countermeasure are retained unchanged by this spec.
- "Consistent structured tool failure" reuses the `{success:false}` shape already used by Map/DataProduct/CSV/ImageGen today, rather than introducing a new envelope.
- **PRD §4.2 cross-check — REQ-CHAT-1 through REQ-CHAT-9**: incremental token-by-token streaming (REQ-CHAT-1) is pre-existing, correctly-functioning baseline behavior underlying FR-009/FR-010 and is not re-specified here. Thread create/list/rename/delete/continue (REQ-CHAT-2) is likewise pre-existing baseline persistence behavior, with the read-only-legacy-thread and multi-chat-specific persistence edges already covered by FR-002 and by spec 006 respectively. Role-based model allow-listing (REQ-CHAT-3) is fully covered by User Story 5 / FR-020–FR-021 (see also spec 014 for registry/catalog management). Configurable message limits (REQ-CHAT-4) are fully covered by User Story 1 / FR-001–FR-006. Markdown/math/code/diagram rendering (REQ-CHAT-6) is pre-existing baseline rendering behavior with no identified defect and is not re-specified here. Citations (REQ-CHAT-7) are fully specified by spec 005, User Story 6 (citation production/persistence from retrieved chunks). Attachments (REQ-CHAT-8) split across two existing surfaces: document attachments routed to retrieval are covered by spec 005's ingestion pipeline; image attachments routed to multimodal models are covered by this spec's FR-022 (pre-flight size validation) with model-side multimodal handling being pre-existing baseline behavior. Feedback capture (REQ-CHAT-9) is split: the ownership-checked, non-persisted proxy-forwarding mechanics are fully specified by spec 017, User Story 3 (FR-008–FR-010); the conversation-rating/per-message-thumb capture shape and configurable sampling rate PRD §4.2 adds on top of that proxy are newly specified here as User Story 8 / FR-030–FR-032, which explicitly defers to spec 017 rather than duplicating its validation/forwarding FRs.
- **PRD §4.16 cross-check — REQ-ARTIFACT-1**: the base artifact-panel capability (generation, dedicated panel display, and state persistence separate from the chat transcript) is newly specified here as User Story 9 / FR-033–FR-035. This is additive to, not a restatement of, the artifact isolation/sandboxing guarantees already specified in User Story 2 / FR-013–FR-016, which continue to govern how each artifact type (`html`/`react`/`svg`) executes safely once displayed in the panel this story defines.
- **PRD §4.7 cross-check — REQ-PERSONA-2**: the persona-invocation flow (start a chat from a persona, applying its full configuration) is newly specified here as User Story 10 / FR-036–FR-039, closing the gap `docs/prd-decomposition-plan.md` flagged after the Personas merge pass found it had no home in spec 009 or spec 010. This spec owns only the invocation/bootstrap behavior; the `PersonaModel` schema itself (fields, validation, authorization) remains owned by spec 009 and is referenced, not redefined, here.
