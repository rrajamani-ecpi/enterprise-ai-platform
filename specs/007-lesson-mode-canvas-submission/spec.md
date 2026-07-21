# Feature Specification: Lesson Mode & Canvas Assignment Submission

**Feature Branch**: `007-lesson-mode-canvas-submission`

**Created**: 2026-07-20

**Status**: Draft

**Input**: Derived from SSD_Document.md §3.3 (Multi-Chat / Chat-Home / Lesson Mode) — Lesson Mode and Canvas-submission material only (Multi-Chat/Chat-Home is covered by a separate spec) — reframed from "as-is" discovery findings into target requirements. Source facts: lesson context is derived from the `/lesson/` URL and the persona is looked up via a cross-account-capable finder (since it may be instructor-owned); non-lesson UI is gated in lesson mode; the URL is forced to `/lesson/{personaId}/{threadId}` once a thread exists; a lesson thread whose stored `personaId` doesn't match the URL persona triggers a redirect back (preventing cross-lesson thread access via URL tampering); Canvas identity for submission is always derived from the server-side session, never the request body; submission generates a PDF, POSTs it to the external Canvas Integration Service (Azure-AD service-to-service auth, retried up to 4 times on 429/5xx/network errors, capped at ~37.5MB), and best-effort writes a submission audit record regardless of outcome; `submit-lesson` hard-gates on `isStudentUser` plus an active Canvas launch context. Two gaps are reframed here: an expired-token check currently uses a best-effort unverified decode that fails OPEN on a parse failure (security gap — must fail closed), and upstream Canvas failures are classified via brittle status-code + free-text-message-substring sniffing (fragile — must become contract-based). This pass additionally incorporates `docs/PRODUCT_REQUIREMENTS_DOCUMENT.md` §4.11 (Canvas/LTI Integration), which supplies REQ-LTI-5 (submitting student work back to the LMS — already covered by FR-006–FR-010) and REQ-LTI-2 (dedicated lesson mode pinning the persona and restricting navigation, with an explicit exit path — the pinning/restriction half is already covered by FR-001–FR-004, but the explicit "exit lesson" affordance was not yet specified and is added here as FR-014/SC-007).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Student completes and submits lesson work to Canvas (Priority: P1)

A Canvas-LTI-launched student works in a lesson-scoped chat thread and submits the resulting work back to the originating Canvas assignment.

**Why this priority**: This is the entire reason lesson mode exists — a student who cannot reliably get graded work back into Canvas gets no value from the feature, regardless of how correct the chat experience around it is. Identity derivation, PDF generation, retry behavior, and audit logging must all work together for a submission to be trustworthy end-to-end.

**Independent Test**: As a properly Canvas-launched student with an active launch context, complete work in a lesson thread and submit; confirm a PDF is generated, POSTed to the external Canvas Integration Service using identity derived solely from the server-side session, and that a submission audit record is written regardless of the outcome.

**Acceptance Scenarios**:

1. **Given** an authenticated student with an active Canvas launch context, **When** they submit lesson work, **Then** the system derives all Canvas identity (course, assignment, user) exclusively from the server-side session — never from any value in the request body — generates a PDF, and POSTs it to the external Canvas Integration Service using Azure-AD service-to-service authentication.
2. **Given** the upstream Canvas Integration Service returns a 429 or 5xx response, or the request fails at the network layer, **When** a submission is in flight, **Then** the system retries the POST up to 4 times before surfacing a failure.
3. **Given** a submission attempt completes, whether successfully or with a failure, **When** the outcome is known, **Then** the system best-effort writes a submission audit record regardless of that outcome.
4. **Given** a caller who is not `isStudentUser`, or who is `isStudentUser` but has no active Canvas launch context, **When** they attempt to submit, **Then** the request is rejected before any PDF generation or upstream call occurs.

---

### User Story 2 - Canvas launch tokens that cannot be parsed are treated as invalid (Priority: P1)

A student's Canvas launch token has expired, been truncated, or is otherwise malformed by the time a submission is attempted.

**Why this priority**: Today, expiry classification uses a best-effort, unverified decode of the launch token, and a parse failure on that decode FAILS OPEN — meaning a token the system cannot even successfully read is treated as not-expired rather than rejected. This is a direct security gap in the same gate that Story 1 depends on, so it must ship alongside the core submission flow, not after it.

**Independent Test**: Present a malformed or undecodable Canvas launch token at submission time and confirm the system treats it as invalid/expired and rejects the submission — it must never be treated as a valid, unexpired token.

**Acceptance Scenarios**:

1. **Given** a well-formed launch token whose expiry has passed, **When** a submission is attempted, **Then** the system classifies it as expired and rejects with `422 CANVAS_TOKEN_EXPIRED`.
2. **Given** a well-formed, unexpired launch token, **When** a submission is attempted, **Then** token classification passes and submission proceeds.
3. **Given** a launch token that cannot be decoded/parsed at all, **When** a submission is attempted, **Then** the system fails closed — treating it as invalid/expired and rejecting the submission — under no circumstance is an unparseable token treated as valid.

---

### User Story 3 - Lesson thread and persona context stay bound to the URL (Priority: P2)

A student navigates directly to a lesson thread URL, including a URL that references a thread belonging to a different lesson persona than the one in the URL.

**Why this priority**: This context-binding is a prerequisite for the submission flow in Stories 1–2 to mean anything (a submission must originate from the correct lesson/assignment context), and it's also the existing defense against URL-tampering across lessons. It's ranked below the submission and token-security stories because it is largely already-correct behavior that needs to be preserved and verified, not repaired.

**Independent Test**: Load a lesson URL whose thread's stored `personaId` does not match the persona segment of the URL, and confirm the system redirects back to `/lesson/{personaId}` rather than rendering the mismatched thread.

**Acceptance Scenarios**:

1. **Given** a URL path starting with `/lesson/`, **When** the page loads, **Then** the system derives lesson context from the URL and looks up the persona via a cross-account-capable finder (so instructor-owned personas resolve correctly for students).
2. **Given** an active lesson session, **When** any non-lesson UI element (voice, extra menu items) would normally appear, **Then** it is gated/hidden.
3. **Given** a lesson conversation that creates a thread, **When** the thread exists, **Then** the URL is forced to `/lesson/{personaId}/{threadId}`.
4. **Given** a lesson thread whose stored `personaId` does not match the persona in the current URL, **When** the page loads, **Then** the system redirects back to `/lesson/{personaId}` instead of exposing the mismatched thread's content.
5. **Given** an active lesson mode session, **When** the student uses the exit-lesson affordance, **Then** the system leaves lesson mode and returns the student to normal (non-lesson) navigation.

---

### User Story 4 - Upstream Canvas submission failures are classified without relying on wording (Priority: P3)

A submission to the external Canvas Integration Service fails for a business reason (student not enrolled, assignment closed, file too large, Canvas unreachable), and the student needs an accurate, actionable error.

**Why this priority**: Today this classification is done by sniffing HTTP status codes plus substrings of the upstream service's free-text error message — a coupling that silently breaks (misclassifying, e.g., a closed assignment as `UNKNOWN`) whenever the upstream service changes its wording, with no test or contract to catch the drift. It's a correctness/support-quality issue rather than a blocker to submitting or a security hole, so it's ranked below Stories 1–3.

**Independent Test**: Simulate upstream responses for each recognized failure reason (not-enrolled, assignment-closed, file-too-large, unreachable) using a defined response contract, then again with the same contract but altered free-text wording, and confirm classification is unaffected by the wording change in both cases.

**Acceptance Scenarios**:

1. **Given** the upstream service returns a response conforming to a defined error contract (e.g., a structured error code field), **When** the system classifies the failure, **Then** it maps to the correct internal reason (`NOT_ENROLLED`/`ASSIGNMENT_CLOSED`/`FILE_TOO_LARGE`/`CANVAS_UNREACHABLE`) based on that contract field, not on message text.
2. **Given** the upstream service changes only the human-readable wording of an existing failure reason's message (contract field unchanged), **When** the system classifies the failure, **Then** classification is unaffected and remains correct.
3. **Given** an upstream response that matches no recognized contract shape, **When** the system classifies the failure, **Then** it is classified `UNKNOWN` without throwing or crashing.

### Edge Cases

- What happens when the generated submission PDF is at or over the ~37.5MB cap — is it rejected pre-flight (classified `FILE_TOO_LARGE`) before any POST attempt is made, rather than failing partway through an upload?
- How does the system behave if the best-effort submission audit-record write itself fails — must this never block or alter the outcome already returned to the student?
- What happens if the Canvas launch context expires in the window between the student initiating submission and the PDF finishing generation?
- How does the system behave when a lesson-thread URL-mismatch redirect (Story 3) occurs while a submission is already in flight for that thread?
- What happens when all 4 retries on a transient upstream failure (Story 1) are exhausted — what specific error and audit outcome does the student see?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: WHEN a request path starts with `/lesson/`, THE SYSTEM MUST derive lesson context from the URL and look up the persona via a cross-account-capable finder (not the normal per-user persona listing), since the persona may be instructor-owned.
- **FR-002**: THE SYSTEM MUST gate non-lesson UI elements (voice, extra menu items) while in lesson mode.
- **FR-003**: WHEN a lesson conversation creates a thread, THE SYSTEM MUST force the URL to `/lesson/{personaId}/{threadId}`.
- **FR-004**: WHEN a lesson thread's stored `personaId` does not match the persona in the current URL, THE SYSTEM MUST redirect back to `/lesson/{personaId}` rather than render the mismatched thread, preventing cross-lesson thread access via URL tampering.
- **FR-005**: THE SYSTEM MUST hard-gate lesson submission on the caller being `isStudentUser` AND holding an active Canvas launch context, rejecting the request before any PDF generation or upstream call if either condition fails.
- **FR-006**: THE SYSTEM MUST derive all Canvas identity used in a submission (course, assignment, user) exclusively from the server-side session, never from the request body.
- **FR-007**: THE SYSTEM MUST generate a PDF of the submitted lesson work and POST it to the external Canvas Integration Service using Azure-AD service-to-service authentication.
- **FR-008**: THE SYSTEM MUST retry the submission POST up to 4 times on 429, 5xx, or network-level errors before surfacing a failure to the caller.
- **FR-009**: THE SYSTEM MUST enforce a submission payload size cap of ~37.5MB, rejecting oversized submissions (classified `FILE_TOO_LARGE`) before attempting the upstream POST.
- **FR-010**: THE SYSTEM MUST best-effort write a submission audit record after every submission attempt, regardless of whether the attempt succeeded or failed.
- **FR-011**: Canvas launch token expiry classification MUST fail closed: any token that cannot be successfully decoded/parsed MUST be treated as invalid/expired and rejected — it MUST NOT be treated as a valid, unexpired token.
- **FR-012**: Upstream Canvas submission failures MUST be classified into `NOT_ENROLLED`/`ASSIGNMENT_CLOSED`/`FILE_TOO_LARGE`/`CANVAS_UNREACHABLE`/`UNKNOWN` using a defined, contract-based signal (e.g., a structured error code) from the upstream response, rather than status-code-plus-free-text-substring sniffing, so classification does not silently break when the upstream service's wording changes.
- **FR-013**: WHEN required Canvas context is missing at submission time, THE SYSTEM MUST reject with `422 CANVAS_CONTEXT_MISSING`.
- **FR-014**: THE SYSTEM MUST provide an explicit, always-available "exit lesson" affordance in lesson mode that returns the student to normal navigation. *(PRD REQ-LTI-2)*

### Key Entities *(include if feature involves data)*

- **Lesson Context**: derived from the active Canvas launch (`canvas_env`, `canvas_user_id`, `canvas_course_id`, `canvas_assignment_id?`, expiry) plus the URL's `personaId`/`threadId` segments; the authoritative source for submission identity.
- **Lesson Thread**: a `ChatThreadModel` with `isLessonThread: true` and a stored `personaId`, scoped to a single lesson/persona pairing.
- **Submission Audit Record**: a best-effort log entry written after every submission attempt, capturing outcome (success/classified failure) independent of whether the upstream call itself succeeded.
- **Canvas Submission Result**: either an upstream success payload (`submission_id`, `canvas_url`) or a classified typed error (`NOT_ENROLLED`/`ASSIGNMENT_CLOSED`/`FILE_TOO_LARGE`/`CANVAS_UNREACHABLE`/`UNKNOWN`) derived from a defined response contract.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of successful submissions in the test suite derive Canvas identity solely from the server-side session, with 0 cases where a request-body-supplied identity value is honored.
- **SC-002**: 0 submission attempts are accepted when presented with a launch token that fails to parse, across a corpus of malformed/truncated/tampered tokens (100% fail-closed rate).
- **SC-003**: 100% of transient upstream failures (429/5xx/network) in the test suite are retried up to 4 times before an error is surfaced to the caller.
- **SC-004**: 100% of upstream failure responses across the `NOT_ENROLLED`/`ASSIGNMENT_CLOSED`/`FILE_TOO_LARGE`/`CANVAS_UNREACHABLE` scenarios are classified correctly via the response contract, with 0 misclassifications when only the upstream free-text wording is changed (contract fields held constant).
- **SC-005**: 100% of lesson-thread requests with a URL/stored-`personaId` mismatch result in a redirect to `/lesson/{personaId}`, with 0 cases of the mismatched thread's content being rendered.
- **SC-006**: 100% of submission attempts (successful or failed) produce a best-effort audit record.
- **SC-007**: 100% of lesson-mode sessions in the test suite expose a working exit-lesson affordance that returns the student to normal navigation on activation.

## Assumptions

- The external Canvas Integration Service's response contract can be extended to carry a structured, versioned error-code field; FR-012 assumes this upstream cooperation rather than inventing classification purely client-side.
- Existing PDF-generation logic and the Azure-AD service-to-service auth mechanism used to reach the Canvas Integration Service are retained as-is; this spec covers the surrounding gating, identity, retry, classification, and audit behavior, not the PDF renderer itself.
- Multi-Chat and Chat-Home (parallel-persona panes, favorites-driven landing page) are explicitly out of scope — covered by a separate spec — even though they share a source directory listing with Lesson Mode in SSD_Document.md §3.3.
- The 90-day Cosmos TTL applied to lesson threads and the underlying Canvas launch/session mechanics (§3.1/§3.4) are unaffected by this spec and are assumed correct as already specified elsewhere.
