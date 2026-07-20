# Feature Specification: Chat Message Pipeline, Tool Safety & Model Access Control

**Feature Branch**: `004-chat-message-pipeline`

**Created**: 2026-07-20

**Status**: Draft

**Input**: Derived from SSD_Document.md §3.2 (Chat Core) — reframed from "as-is" discovery findings into target requirements, scoped to the message-limit, prompt-assembly, PII-redaction, tool-orchestration, model-access-control, artifact-security, and sharing material (document ingestion/retrieval is out of scope — covered separately). Source facts: `messageLimitPreflight` runs before any thread/message record is touched and caps fail open on config/counter read failure; client-supplied `dataProducts` are always discarded server-side in favor of the thread's stored values (anti-IDOR); HTML artifacts render in a real sandboxed iframe but React artifacts execute via `react-live` in the same JS realm as the app despite design docs claiming otherwise; shared-thread access checks only "is the caller logged in" (not "is the caller the intended recipient") and shares never expire; tool-specific failure handling is inconsistent (Calculator/Weather let exceptions propagate raw while Map/DataProduct/CSV/ImageGen return a structured `{success:false}` payload); model-access enforcement silently substitutes the default model when a requested model isn't in the caller's role allow-list.

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

### Edge Cases

- Should the silent default-model substitution (User Story 5) become user-visible (e.g., a notice that the requested model was unavailable and a substitute was used), or should it remain silent as today? This spec keeps the substitution behavior itself unchanged and flags the visibility question as open rather than mandating a UI change.
- What happens when a share is accessed concurrently with the owner revoking/expiring it — is there a race where an in-flight clone completes after expiry takes effect?
- How should PII redaction interact with a message that is blocked by a cap — does redaction ever run on text that is never actually sent (it must not, since redaction is scoped only to user-authored text actually sent to the model)?
- What is the correct behavior when a tool times out (30s default, up to 150s override) versus when it throws — do both now funnel into the same structured failure shape from Story 4, or is "timed out" treated as a distinct, still-consistent case?
- How does artifact isolation for React interact with legitimate use cases that currently rely on same-realm access (if any) — does closing the isolation gap break any existing artifact functionality that assumed shared-realm behavior?
- What happens when a recipient's identity changes (e.g., email change) between share creation and access — does recipient-matching need to tolerate identity drift?

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

### Key Entities *(include if feature involves data)*

- **ChatThreadModel**: owned chat thread; `version` (`v1`/`v2`/`v3`, only `v3` writable), stored `dataProducts`, sharing fields (`isShared`, `shareId`, `sharedBy`, `sharedAt`, `clonedFromShareId`). Extended by this spec to require a recorded intended recipient and an expiry for any active share.
- **ChatMessageModelV2**: Cosmos document holding `messages: AcceleratorUIMessage[]`; unaffected in shape by this spec, but its creation is gated entirely by the preflight checks in Story 1.
- **ToolResult**: the structured outcome of any tool invocation; standardized by this spec to `{success: boolean, data?, error?}` across all tools, replacing today's raw-exception path for Calculator/Weather.
- **Artifact**: a model-generated renderable object of type `html` | `react` | `svg`; each type's isolation/sanitization guarantee is defined explicitly by this spec (FR-013–FR-016).
- **SystemModelConfig / ModelConfigDocument**: role allow-lists and per-model flags (`requiresAdvancedModelAccess`, `isEnabled`) that drive the model-substitution gate in FR-020/FR-021.

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

## Assumptions

- Document ingestion, retrieval/RAG, and PDF export are out of scope for this spec (covered separately); this spec covers only the message-limit, prompt-assembly, PII-redaction, tool-orchestration, model-access, artifact-security, and sharing surfaces of Chat Core.
- Introducing a recorded "intended recipient" on a share is a schema addition to `ChatThreadModel`'s sharing fields (today it is only `isShared`/`shareId`/`sharedBy`/`sharedAt`); this spec assumes recipient identity is captured at share-creation time (e.g., an email, consistent with sharing patterns used elsewhere in the codebase for personas/prompts) rather than introducing a new sharing-permission model from scratch.
- The specific mechanism used to achieve genuine React artifact isolation (e.g., moving React artifact rendering into the same sandboxed-iframe approach as HTML, or an equivalent isolated-realm technique) is an implementation decision; this spec only mandates the resulting isolation property, not the technique.
- The default-model substitution behavior in User Story 5 is retained as-is; whether to make the substitution visible to the end user is explicitly called out as an open edge case, not decided by this spec.
- Existing Azure App Service idle-stream behavior (~60s reset) and the 15-second keepalive countermeasure are retained unchanged by this spec.
- "Consistent structured tool failure" reuses the `{success:false}` shape already used by Map/DataProduct/CSV/ImageGen today, rather than introducing a new envelope.
