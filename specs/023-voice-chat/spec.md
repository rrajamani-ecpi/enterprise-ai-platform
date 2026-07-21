# Feature Specification: Voice Chat

**Feature Branch**: `023-voice-chat`

**Created**: 2026-07-21

**Status**: Draft

**Input**: Derived from `docs/PRODUCT_REQUIREMENTS_DOCUMENT.md` §4.15 (Voice Chat), REQ-VOICE-1 — the sole PRD requirement for this capability: a real-time voice conversation mode using a realtime speech model, with secure session/token negotiation (WebRTC-style offer/answer). No existing spec-kit spec covers this capability; it is novel enough (realtime session/token negotiation) to warrant its own spec rather than merging into an existing one, per `docs/prd-decomposition-plan.md`'s merge/new routing. Role-based gating of which models (including realtime voice models) a caller may use is already specified in `specs/014-model-access-config-management/spec.md` and is cross-referenced here rather than re-specified.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Start a real-time voice conversation via a secure, server-issued session (Priority: P1)

A signed-in user starts a voice conversation. The client never holds a long-lived provider secret; instead it asks the server for a short-lived session/token, then uses that to negotiate a real-time (WebRTC-style offer/answer) connection directly with the realtime speech model endpoint.

**Why this priority**: This is the entire capability described by REQ-VOICE-1 and the only PRD-stated requirement in this area. Without a working, secure session/token negotiation there is no voice chat feature at all — everything else in this spec is refinement around this one flow. Per Constitution Principle II ("Explicit, Server-Side Authorization for Every Access"), model/entitlement selection must be resolved server-side, and per the workload-identity pattern already established for model provider authentication (PRD REQ-MODEL-5; spec 014 FR-013), no long-lived provider secret may ever reach the client.

**Independent Test**: As an authenticated, authorized user, request a voice session; confirm the server returns a short-lived token/session descriptor (not a durable provider API key), and that this artifact is sufficient to complete offer/answer negotiation with the realtime model endpoint.

**Acceptance Scenarios**:

1. **Given** an authenticated, authorized user requests to start a voice conversation, **When** the client calls the session/token endpoint, **Then** the server issues a short-lived, single-use session credential scoped to that conversation, and the underlying long-lived provider secret is never returned to the client.
2. **Given** a valid short-lived session credential, **When** the client performs offer/answer negotiation with the realtime speech model endpoint, **Then** a real-time voice conversation session is established.
3. **Given** a session credential that has expired or was already consumed, **When** it is presented for negotiation, **Then** the negotiation is rejected.

---

### User Story 2 - Voice mode access is gated by role-based model access control (Priority: P2)

An admin has configured which roles may use a given realtime voice model, the same way access to any other chat model is configured. A user attempts to start a voice conversation; the server resolves whether that user's role is permitted to use the configured realtime voice model before issuing any session/token.

**Why this priority**: Voice conversation uses a model like any other in the registry, and reuses the access-control mechanism already specified in full in spec 014 (role allow-lists intersected with `isEnabled` and, where applicable, `advancedModelAccess`) rather than introducing a parallel gating mechanism. This is ranked below Story 1 because the negotiation flow must exist before gating it matters, but it is required before general availability since voice models may carry cost/compliance considerations similar to other advanced models.

**Independent Test**: Configure a realtime voice model as enabled for one role's allow-list but not another's; confirm a user in the permitted role can obtain a session/token and a user in the non-permitted role is rejected before any session/token is issued — independent of any client-side UI gating.

**Acceptance Scenarios**:

1. **Given** a realtime voice model that is enabled and present in the caller's role allow-list (per spec 014's access computation), **When** the caller requests a voice session, **Then** the session/token endpoint issues a session credential.
2. **Given** a realtime voice model that is disabled, absent from the caller's role allow-list, or requires advanced access the caller lacks, **When** the caller requests a voice session, **Then** the request is rejected server-side before any session/token is issued, regardless of whether the client UI exposed the voice option.

---

### User Story 3 - Voice session survives or cleanly ends on connection drop (Priority: P3)

A user's real-time voice connection drops mid-conversation (network blip, tab backgrounded, device switch). The system either renegotiates the session or terminates it cleanly, without leaving orphaned server-side session state or requiring the user to re-authenticate from scratch.

**Why this priority**: This is not literally stated in the one-line PRD requirement, but is an inferred, reasonable requirement — any real WebRTC-style realtime session implementation must define what happens when the underlying connection drops, or it risks orphaned sessions, stuck credentials, or a confusing user experience. Ranked P3 as a robustness/edge-case concern layered on top of the core negotiation (Story 1) and gating (Story 2) flows.

**Independent Test**: Establish a voice session, forcibly drop the underlying connection, and confirm that either (a) the client can renegotiate using a fresh session/token without a full re-authorization round-trip failure, or (b) the session is terminated and its server-side state is cleaned up within a bounded time, with no dangling session credential left valid.

**Acceptance Scenarios**:

1. **Given** an established voice session, **When** the underlying real-time connection drops unexpectedly, **Then** the system either renegotiates a new connection under a fresh session credential or terminates the session and releases its server-side state.
2. **Given** a session that has been terminated (by drop, timeout, or explicit end), **When** its prior session credential is reused, **Then** the reuse is rejected.

### Edge Cases

- What happens when a user requests a new voice session while a previous session for the same user is still active/unterminated?
- How does the system handle a session/token request for a realtime voice model that has been disabled by an admin after the client loaded the page but before the request is made?
- What happens when offer/answer negotiation is attempted with a session credential whose scope (e.g., conversation id) doesn't match the negotiation request?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST expose a session/token endpoint that issues a short-lived, single-use session credential for real-time voice conversation, resolved and authorized server-side, per Constitution Principle II.
- **FR-002**: The system MUST NOT deliver any long-lived provider secret to the client at any point in the voice conversation flow, consistent with the workload-identity pattern established for model provider authentication (PRD REQ-MODEL-5; spec 014 FR-013).
- **FR-003**: The system MUST support WebRTC-style offer/answer session negotiation using the server-issued session credential to establish a real-time voice conversation with the realtime speech model.
- **FR-004**: A session credential MUST be scoped (e.g., to a specific conversation/session) and MUST be rejected if expired, already consumed, or presented outside its intended scope.
- **FR-005**: The system MUST resolve whether a caller's role may use the configured realtime voice model server-side, before issuing any session/token, reusing the model-access computation already specified in `specs/014-model-access-config-management/spec.md` (role allow-list intersected with `isEnabled` and, where applicable, `advancedModelAccess`) rather than a separate gating mechanism.
- **FR-006**: Client-side UI gating of voice mode availability MUST be advisory only; the server-side check in FR-005 is the sole authorization boundary, per Constitution Principle II.
- **FR-007**: When the underlying real-time connection for an active voice session drops, the system MUST either support renegotiation under a fresh session credential or terminate the session and release its server-side state within a bounded time.
- **FR-008**: A terminated or expired session credential MUST NOT be usable to establish or resume a voice connection.

### Key Entities *(include if feature involves data)*

- **VoiceSession**: a real-time voice conversation instance — session credential (short-lived, single-use, scoped), associated user/conversation identity, realtime voice model reference, status (`pending` | `active` | `terminated`), creation/expiry timestamps.
- **VoiceSessionCredential**: the short-lived, server-issued artifact used by the client to perform offer/answer negotiation with the realtime speech model endpoint; never a durable provider secret.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of voice session/token requests from authorized callers result in a session credential sufficient to complete negotiation, with 0% of responses containing a long-lived provider secret.
- **SC-002**: 100% of voice session requests from callers whose role/model-access resolution fails (per spec 014's computation) are rejected before any session credential is issued.
- **SC-003**: 100% of expired or already-consumed session credentials are rejected on reuse attempts across a test corpus.
- **SC-004**: 100% of simulated connection drops during an active voice session result in either successful renegotiation or full server-side session-state cleanup within a defined bounded time, with 0 dangling valid credentials left afterward.

## Assumptions

- The specific realtime speech model/provider is not specified by the PRD and is out of scope for this spec; this spec covers the session/token negotiation and access-gating contract, not the choice of provider.
- Telephony/PSTN integration (dial-in/dial-out voice) is out of scope; this spec covers browser/client-based real-time voice conversation only, consistent with the PRD's WebRTC-style framing.
- Which roles/models are permitted to use voice mode is a configuration concern fully covered by `specs/014-model-access-config-management/spec.md`'s role allow-list mechanism; this spec does not introduce a separate voice-specific access-control model.
- Conversation transcript persistence, PII redaction, and message-history semantics for voice conversations are assumed to reuse whatever mechanism already governs chat conversations elsewhere in the platform, and are not re-specified here.
- The bounded time for session cleanup on connection drop (Story 3 / FR-007) is intentionally left as an implementation-level parameter rather than a PRD-specified value, since the PRD does not state one.
