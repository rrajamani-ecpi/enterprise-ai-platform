# Feature Specification: AI-Assisted Persona Builder & Live Preview

**Feature Branch**: `010-persona-builder-live-preview`

**Created**: 2026-07-20

**Status**: Draft

**Input**: Derived from SSD_Document.md §3.5 (Persona Management & Persona Studio) — reframed from "as-is" discovery findings into target requirements, scoped specifically to the AI-assisted builder (`/api/persona-builder`) and live draft preview (`/api/persona-preview`); general persona CRUD/authorization/sharing behavior is covered by a separate spec. Source facts: the builder uses structured-output generation (`generateObject`) that only emits fields it's confident about (omitted means leave-unchanged), never fabricates a persona name, and recommends a model only from an allow-listed catalog (Opus models excluded from AI recommendations, though a human can still pick one manually); both endpoints return structured 400/500 errors for invalid payload, missing conversation turns, no compatible model, or generation failure, with content-filter/rate-limit/network classification logged. The notable gap: live preview streams a *real* chat completion against the draft (unsaved) persona config, with the same tools actually executing *live* — not mocked — despite the system prompt telling the model otherwise, even though several tools (web search, external API calls, image generation) have genuine real-world side effects.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Generate or refine persona fields from a conversation (Priority: P1)

A persona author describes what they want ("a Socratic tutor for intro statistics students") in a chat-style builder UI, and the system proposes persona fields (name, description, system prompt, etc.) without overwriting fields the author didn't ask to change.

**Why this priority**: This is the feature's entire reason to exist. If the assistant fabricates data (especially a persona name) or clobbers fields the author never mentioned, it's actively harmful rather than merely unhelpful — trust in every other capability of the builder depends on getting this right first.

**Independent Test**: Submit a conversation that clearly describes some persona attributes but not others, confirm the response includes only the confidently-derivable fields, and confirm a conversation that never establishes a name yields no fabricated name.

**Acceptance Scenarios**:

1. **Given** a build request whose conversation clearly describes a customer-support persona's tone and purpose but never mentions a name, **When** generation completes, **Then** the response omits the `name` field entirely rather than inventing one.
2. **Given** an editing session where the author asks only to adjust the description, **When** generation completes, **Then** fields not addressed in the conversation (e.g., `model`, `name`, `extensions`) are absent from the response, and the client preserves their existing values rather than clearing them.
3. **Given** a build request with a clearly and completely described persona, **When** generation completes, **Then** all confidently-derivable fields (name, description, system prompt) are populated in a single response.

---

### User Story 2 - Model recommendations stay within policy (Priority: P1)

The builder suggests a model for the persona being created, drawn only from models the organization has approved for AI-driven recommendation — never a model that requires explicit human judgment to select (e.g., an Opus-family model).

**Why this priority**: An unconstrained recommendation could steer authors toward a premium/expensive or policy-restricted model without a human deliberately choosing it. This must hold on day one alongside Story 1, since the two together define what "safe to recommend" means for the builder.

**Independent Test**: Issue build requests asking for a model recommendation and confirm every recommended model id belongs to the allow-listed persona-generation catalog and is never an Opus-family model; separately confirm a human can still manually assign an Opus model to a persona outside the AI flow.

**Acceptance Scenarios**:

1. **Given** a build request asking for a model recommendation, **When** generation completes, **Then** the recommended model id is a member of the allow-listed persona-generation catalog.
2. **Given** the persona-generation allow-list excludes all Opus-family models, **When** generation runs, **Then** no Opus-family model id is ever returned as an AI recommendation, regardless of conversation content.
3. **Given** a user manually selects an Opus-family model in Persona Studio (not via the AI recommendation), **When** they save the persona, **Then** the manual selection is accepted unchanged.

---

### User Story 3 - Predictable, classified errors from both endpoints (Priority: P2)

A caller (UI or direct API) sends an invalid or empty request, or the underlying model call fails, to either `/api/persona-builder` or `/api/persona-preview`.

**Why this priority**: Once generation itself is trustworthy (Stories 1–2), predictable error contracts are what let the UI show actionable messages instead of a generic failure, and let operators triage recurring failures (content-filter vs. rate-limit vs. network) from logs. This is foundational plumbing rather than a standalone user-facing capability, so it ranks just below the generation-correctness stories.

**Independent Test**: Send a malformed payload, a payload with zero conversation turns, and a request when no compatible model is configured, to both endpoints; separately simulate a content-filter/rate-limit/network failure and confirm the failure is logged with its specific classification.

**Acceptance Scenarios**:

1. **Given** a request missing a required field, **When** either endpoint is called, **Then** the response is a structured 400 error identifying the invalid field.
2. **Given** a request with zero conversation turns, **When** either endpoint is called, **Then** the response is a structured 400 error stating that conversation turns are required.
3. **Given** no model compatible with generation is currently available, **When** either endpoint is called, **Then** the response is a structured error explicitly identifying "no compatible model," not a generic failure.
4. **Given** the underlying model call fails due to content filtering, rate limiting, or a network error, **When** either endpoint is called, **Then** the response is a structured 500 error and the failure is logged with its specific classification (content-filter | rate-limit | network).

---

### User Story 4 - Live preview reflects the unsaved draft, honestly (Priority: P2)

A persona author, mid-edit in Persona Studio, sends a message to the live preview to see how the *draft* configuration (not yet saved) would actually behave — including any tools the draft enables.

**Why this priority**: Preview is where authors validate a persona before committing it, so it must reflect the true draft config. The known gap — real tool execution while the model is told it's simulated — is a transparency problem, not a "preview doesn't work" problem: authors and platform operators need to know when a preview click can trigger a real web search, a real external API call, or real image generation (and its cost/side effects), rather than assuming preview is inherently sandboxed. Blocking or mocking tool execution during preview was considered and rejected: it would defeat the purpose of a *live* preview (verifying real tool behavior before save) and reduce authors' ability to catch integration problems before shipping. The more defensible fix is transparency — stop telling the model a fiction, and make real-world effects visible to the human — not silently mocking or refusing execution.

**Independent Test**: Edit a persona's draft config (system prompt, model, or an extension) without saving, enable a side-effecting extension (e.g., web search or image generation), send a preview message that triggers that tool, and confirm (a) the response reflects the draft config rather than the last-saved version, and (b) the tool call is clearly labeled to the user as live/real, not simulated.

**Acceptance Scenarios**:

1. **Given** an unsaved persona draft with a modified system prompt, model, or extension set, **When** the user sends a preview message, **Then** the completion is generated against the draft configuration, not the last-saved persona record.
2. **Given** a draft persona with a side-effecting extension enabled (e.g., web search, image generation, or another externally-acting tool), **When** the model invokes that tool during preview, **Then** the UI clearly labels the invocation as live/real (not simulated) at or before the point its result is shown, and the model-facing system prompt no longer claims tool execution is simulated.
3. **Given** a draft persona with only non-side-effecting extensions enabled (e.g., calculator, RAG search over the author's own already-ingested data), **When** those tools are invoked during preview, **Then** no additional live-effect labeling beyond normal tool-call UI is required.
4. **Given** a preview generation fails, **When** the failure is due to invalid payload, missing turns, no compatible model, content filtering, rate limiting, or a network error, **Then** the caller receives the same structured, classified error contract defined in User Story 3.

### Edge Cases

- What happens when a build request's conversation doesn't change any field relative to the persona's current state? (Response should reflect an empty/no-op diff, not re-emit unchanged values as if newly generated.)
- What happens when structured-output generation returns a partially malformed field (e.g., an unrecognized value for a constrained field like `model`)? (Must be treated as a generation failure for that field, not silently coerced or passed through.)
- How does live preview behave when the draft config enables the `DataProduct` extension but references data products that aren't yet saved/associated with the persona?
- What is the expected behavior if a live preview session executes a side-effecting tool (e.g., generates an image, performs a paid web search) and the author then discards the draft without saving — is there any record that a real side effect occurred, for cost/audit purposes?
- What happens when the draft persona's selected model doesn't support structured output, tool use, or streaming required by the builder/preview endpoints? (Should surface as the "no compatible model" error class from User Story 3, not a generic failure.)

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The persona-builder endpoint MUST use structured-output generation that emits only the fields it is confident about; any field omitted from the response MUST be interpreted by the caller as "leave unchanged," never as "clear this field."
- **FR-002**: The persona-builder endpoint MUST NOT fabricate a persona name; when the conversation does not clearly establish a name, the `name` field MUST be omitted from the response.
- **FR-003**: Any model recommended by the persona-builder endpoint MUST be selected only from the allow-listed persona-generation model catalog.
- **FR-004**: Opus-family models MUST be excluded from AI-generated model recommendations; a human MUST retain the ability to manually assign an Opus-family model to a persona outside the AI recommendation flow.
- **FR-005**: Both the persona-builder and persona-preview endpoints MUST return a structured 400 error, identifying the specific problem, for an invalid payload or a payload with zero conversation turns.
- **FR-006**: Both endpoints MUST return a structured error explicitly classified as "no compatible model" when no model capable of servicing the request is available, rather than a generic failure.
- **FR-007**: Both endpoints MUST return a structured 500 error for generation failures, and MUST log each such failure with a specific classification of content-filter, rate-limit, or network.
- **FR-008**: The persona-preview endpoint MUST generate its chat completion against the caller-supplied draft (unsaved) persona configuration, not the last-saved persona record.
- **FR-009**: The persona-preview endpoint's model-facing system prompt MUST accurately describe tool-execution behavior; it MUST NOT state or imply that tool calls are simulated/mocked when they in fact execute live.
- **FR-010**: When a tool invoked during live preview has real-world side effects (e.g., web search, image generation, or another externally-acting call), the UI MUST clearly label that invocation to the user as live/real, at or before the point its result is displayed.
- **FR-011**: Tools with no real-world side effects (e.g., calculator, RAG search over the author's own already-ingested data) are exempt from the live-effect labeling in FR-010.

### Key Entities *(include if feature involves data)*

- **PersonaBuilderRequest/Response**: a conversation (prior turns) in, a partial `PersonaModel` field set out — only confidently-derivable fields are present; absent fields signal "unchanged."
- **PersonaDraftConfig**: the in-flight, unsaved candidate persona configuration (system prompt, model, extensions, data products) supplied per-request to live preview; distinct from the persisted `PersonaModel`.
- **Persona-Generation Model Catalog**: the allow-listed subset of the model registry eligible for AI recommendation (a stricter filter than the general per-role model allow-list), explicitly excluding Opus-family models.
- **ToolInvocation (preview context)**: a tool call made during a live-preview completion, classified as side-effecting (real-world consequence) or non-side-effecting for labeling purposes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Across a test corpus of partial-information conversations, 100% of persona-builder responses omit every field not confidently derivable from the conversation.
- **SC-002**: Across a test corpus of conversations that never establish a persona name, 0% of persona-builder responses contain a fabricated `name`.
- **SC-003**: 100% of AI-generated model recommendations fall within the allow-listed persona-generation catalog, and 0% are Opus-family models, across a test corpus of build requests.
- **SC-004**: 100% of invalid-payload, missing-turns, and no-compatible-model requests to either endpoint receive the specific structured error class defined for that condition (not a generic 500), across a test corpus covering both endpoints.
- **SC-005**: 100% of simulated content-filter/rate-limit/network failures are logged with the correct classification, across a test corpus covering both endpoints.
- **SC-006**: 100% of live-preview tool invocations from side-effecting extensions (web search, image generation, other externally-acting tools) are visibly labeled to the user as live/real, across a test corpus covering each side-effecting extension.
- **SC-007**: An audit of the persona-preview system prompt confirms 0 statements contradicting actual tool-execution behavior.

## Assumptions

- The persona-generation model allow-list catalog and its Opus exclusion are managed as existing admin configuration (Domain H, Admin/Settings); this spec assumes the builder consumes that catalog rather than redefining how it is administered.
- General persona CRUD, authorization (`EnsurePersonaOperation`), sharing, and ownership-transfer behavior are covered by a separate spec; this spec assumes whatever authorization gate applies to persona read/write access is applied unchanged to the builder and preview endpoints.
- Live preview continues to execute tools for real against external services (not moved to a mocked/sandboxed mode) — this is a deliberate choice (see User Story 4) to preserve preview's value in catching real integration issues before save; the fix scoped here is truthful system-prompt framing and user-facing labeling, not blocking execution.
- The draft persona configuration used for preview is supplied per-request by the client and is not itself persisted as a side effect of calling the preview endpoint.
