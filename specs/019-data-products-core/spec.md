# Feature Specification: Data Products — Core Capability

**Feature Branch**: `019-data-products-core`

**Created**: 2026-07-21

**Status**: Draft

**Input**: Derived from `docs/PRODUCT_REQUIREMENTS_DOCUMENT.md` §4.6 (Data Products) and `docs/prd-decomposition-plan.md`'s call-out that specs 012 (authorization hardening) and 013 (ingestion pipeline reliability) are both hardening-only, drafted from `docs/SSD_Document.md`'s as-is discovery findings, and that neither specifies the PRD's full base CRUD/versioning/MCP-exposure/bulk-ingestion capability. This spec is that missing base spec: create/rename/delete a data product, upload files into it, enable/disable individual documents, version documents, monitor ingestion health, rate-limit ingestion/query endpoints, audit significant actions, and optionally expose a data product via MCP. It defines the base capability only — authorization/security enforcement is spec 012's concern, ingestion-pipeline reliability (retry/dead-letter) is spec 013's concern, and sharing/persona-attachment rules are spec 018's concern; this spec cross-references all three rather than re-specifying them.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create, rename, delete a data product and manage its documents (Priority: P1)

A user creates a data product (a named, ownable collection of documents used for grounding), renames it, uploads files into it, and can enable or disable individual documents without deleting them — e.g., to temporarily exclude an outdated document from retrieval while keeping it available to re-enable later.

**Why this priority**: This is the base capability everything else in this spec and in specs 012/013/018 depends on — there is no authorization surface to harden (012), no pipeline to make reliable (013), and nothing to share (018) until a data product and its documents exist. It must ship first as the MVP.

**Independent Test**: As an authorized user, create a data product, rename it, upload a file into it, disable that document, confirm it no longer surfaces in grounding/retrieval while still listed and re-enablable, then delete the data product.

**Acceptance Scenarios**:

1. **Given** an authorized user, **When** they create a data product with a name and description, **Then** a new data product record exists, owned by that user, containing no documents.
2. **Given** an existing data product, **When** its owner renames it, **Then** the new name is persisted and reflected everywhere the data product is listed.
3. **Given** an existing data product, **When** an authorized user uploads one or more files into it, **Then** each file becomes a document entry associated with that data product and enters the ingestion pipeline.
4. **Given** a document within a data product, **When** an authorized user disables it, **Then** the document is excluded from grounding/retrieval but remains listed and can be re-enabled later without re-uploading.
5. **Given** a data product, **When** its owner deletes it, **Then** the data product and its documents are no longer available for grounding, retrieval, or listing.

---

### User Story 2 - Documents are versioned as they change (Priority: P2)

A user re-uploads a document to replace an outdated version, or the same source file changes over time (e.g., via bulk ingestion), and the system tracks that the document has multiple versions rather than silently overwriting history.

**Why this priority**: Versioning protects against silent content loss and is necessary to reason about "what changed and when" for a collection, but a data product is already usable end-to-end (Story 1) before this exists — it refines the base capability rather than gating it.

**Independent Test**: Upload a document, re-upload a new version of the same document, and confirm the data product records more than one version for that document with the current version used for retrieval.

**Acceptance Scenarios**:

1. **Given** a document already in a data product, **When** a new version of that document is uploaded (manually or via bulk ingestion), **Then** the system records a new version rather than discarding the version history.
2. **Given** a document with multiple versions, **When** grounding/retrieval runs against the data product, **Then** the current version's content is used.
3. **Given** a document's version history, **When** an authorized user inspects it, **Then** they can see that more than one version exists and which is current.

---

### User Story 3 - Expose a data product via MCP for external agents/tools (Priority: P2)

An owner enables MCP exposure on a data product so external agents/tools (outside this platform) can query it as an MCP-compliant resource.

**Why this priority**: This is an optional, additive capability layered on top of an already-functioning data product (Story 1) — valuable for external integration scenarios but not required for the platform's own in-app grounding use, so it ranks below the base CRUD story.

**Independent Test**: Enable MCP exposure on a data product, then query it from an external MCP client and confirm it returns grounded results; disable MCP exposure and confirm the endpoint no longer serves that data product.

**Acceptance Scenarios**:

1. **Given** a data product with MCP exposure disabled (the default), **When** an external MCP client attempts to query it, **Then** the request does not succeed.
2. **Given** an owner enables MCP exposure on a data product, **When** an external MCP client queries it with valid credentials, **Then** it receives grounded results from that data product's documents.
3. **Given** MCP exposure is enabled, **When** the owner disables it, **Then** subsequent external MCP queries against that data product no longer succeed.
4. **Given** an MCP-exposed data product, **When** authorization is evaluated for an external caller, **Then** it defers to the canonical authorization/API-key checks defined in spec 012 rather than reimplementing them here.

---

### User Story 4 - Ingestion and query operations are rate-limited (Priority: P2)

A caller (internal user or, for MCP, an external agent) issues a high volume of ingestion or query requests against a data product, and the system throttles excess requests rather than allowing unbounded load.

**Why this priority**: Rate limiting protects shared infrastructure (extraction, embedding, retrieval) from being overwhelmed by a single data product or caller — important for platform stability, but the underlying operations must exist (Story 1) before there is anything to rate-limit.

**Independent Test**: Issue ingestion requests and query requests against a data product at a rate exceeding the configured limit, and confirm excess requests are rejected or throttled rather than all being processed.

**Acceptance Scenarios**:

1. **Given** a configured rate limit for ingestion operations, **When** a caller exceeds it within the configured window, **Then** subsequent ingestion requests in that window are rejected with a rate-limit response, not silently queued or dropped.
2. **Given** a configured rate limit for query operations, **When** a caller exceeds it within the configured window, **Then** subsequent query requests in that window are rejected with a rate-limit response.
3. **Given** a caller within the configured limits, **When** they issue ingestion or query requests, **Then** all requests are processed normally with no throttling applied.

---

### User Story 5 - Significant actions are audited (Priority: P3)

An administrator or auditor needs to review who created, uploaded to, or deleted a data product's documents, and when.

**Why this priority**: Audit logging is valuable for accountability and incident investigation but is passive/observational — it doesn't change what users can do with a data product, so it's the lowest priority in this spec.

**Independent Test**: Create a data product, upload a document, then delete the data product, and confirm each action produced a corresponding audit record with actor, action, target, and timestamp.

**Acceptance Scenarios**:

1. **Given** a data product is created, **When** the create completes, **Then** an audit record is written capturing the actor, the action ("create"), the target data product, and a timestamp.
2. **Given** a document is uploaded to a data product, **When** the upload completes, **Then** an audit record is written capturing the actor, the action ("upload"), the target document/data product, and a timestamp.
3. **Given** a data product or document is deleted, **When** the delete completes, **Then** an audit record is written capturing the actor, the action ("delete"), the target, and a timestamp.
4. **Given** an auditor reviews audit records for a data product, **When** they inspect the history, **Then** they can reconstruct the sequence of create/upload/delete actions taken against it.

### Edge Cases

- What happens when a document is disabled and then the same document is re-uploaded (new version)? The new version should become current and enabled by default; disabling one version must not silently apply to a subsequently uploaded version.
- What happens when a data product is deleted while an MCP client holds a previously-valid API key for it? Subsequent MCP queries must fail (see spec 012 Story 4 for the specific non-2xx access-boundary behavior).
- What happens when a rate limit is hit mid-bulk-ingestion (see spec 013's bulk-ingestion pipeline)? The base ingestion-health signal (this spec) and the reliability/retry mechanics (spec 013) must remain distinguishable: a rate-limit rejection is not itself a job failure requiring dead-letter handling.
- How does version history behave for documents arriving via automated bulk ingestion (spec 013) versus manual upload? Both paths MUST produce the same versioning behavior (Story 2) — the ingestion source does not change how versions are recorded.
- What happens when audit logging fails to write (e.g., transient storage error)? The triggering action (create/upload/delete) itself must not be blocked or rolled back solely because the audit write failed; this is a logging concern, not a transactional one.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST let authorized users create a data product with a name and description. *(REQ-DP-1)*
- **FR-002**: The system MUST let an authorized user rename an existing data product.
- **FR-003**: The system MUST let an authorized user delete a data product, subject to the deletion authorization rules defined in spec 012.
- **FR-004**: The system MUST let authorized users upload one or more documents into a data product. *(REQ-DP-1)*
- **FR-005**: The system MUST let authorized users enable or disable an individual document within a data product without deleting it; a disabled document MUST be excluded from grounding/retrieval while remaining listed and re-enablable. *(REQ-DP-2)*
- **FR-006**: The system MUST version documents such that a new upload of an existing document creates a new version rather than discarding prior version history, with grounding/retrieval using the current version. *(REQ-DP-3)*
- **FR-007**: The system MUST track ingestion health per data product (e.g., per-document ingestion status) and surface ingestion failures to the data product's owner; the underlying retry/dead-letter recovery mechanics for pipeline-level failures are defined in spec 013. *(REQ-DP-3)*
- **FR-008**: The system MUST rate-limit ingestion operations per data product/caller according to a configured limit, rejecting requests that exceed it within the configured window. *(REQ-DP-4)*
- **FR-009**: The system MUST rate-limit query operations per data product/caller according to a configured limit, rejecting requests that exceed it within the configured window. *(REQ-DP-4)*
- **FR-010**: The system MUST record an audit entry (actor, action, target, timestamp) for create, upload, and delete actions on data products and their documents. *(REQ-DP-5)*
- **FR-011**: The system MUST support sharing a data product with individuals/groups and attaching it to personas per the canonical sharing policy defined in spec 018, rather than implementing separate sharing rules here. *(REQ-DP-6)*
- **FR-012**: The system MUST let an owner optionally expose a data product via an MCP endpoint, with the endpoint disabled by default and enforcing the authorization/API-key rules defined in spec 012. *(REQ-DP-7)*
- **FR-013**: The system MUST support automated bulk ingestion of documents into a data product from an enterprise content source (e.g., SharePoint via Logic Apps) in addition to manual upload, using the same versioning (FR-006) and ingestion-health tracking (FR-007) as manual upload; retry/dead-letter reliability for this pipeline is defined in spec 013. *(REQ-DP-8)*

### Key Entities *(include if feature involves data)*

- **DataProductModel**: an owned, named collection of documents used for grounding — `id`, `name`, `description`, owner, `mcpEnabled` (default false). Authorization/visibility rules for this entity are defined in spec 012; sharing rules in spec 018.
- **DataProductDocumentModel**: a document within a data product — `id`, `dataProductId`, `fileName`/`url`, `enabled` (bool, default true), `ingestionStatus`, current version pointer.
- **DocumentVersion**: a versioned snapshot of a document's content, created on each upload/re-upload (manual or bulk); one is marked current per document.
- **AuditRecord**: an entry capturing actor, action (create/upload/delete), target (data product or document), and timestamp.
- **RateLimitPolicy**: a configured request-rate ceiling applied per data product/caller to ingestion and query operations independently.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of created data products are immediately listable, renamable, and deletable by their owner, with 0 drawn/uploaded documents lost between upload and listing.
- **SC-002**: 100% of disabled documents are excluded from grounding/retrieval results while remaining visible in the data product's document list, and 100% of re-enabled documents return to grounding/retrieval without re-upload.
- **SC-003**: 100% of re-uploads of an existing document produce a new retrievable version while preserving prior version records, verified across manual-upload and bulk-ingestion test corpora.
- **SC-004**: 100% of ingestion failures for a data product's documents are visibly surfaced as a non-success ingestion status to the owner within the current session/page view.
- **SC-005**: 100% of ingestion and query requests exceeding the configured rate limit are rejected rather than processed, with 0 impact on requests within the limit.
- **SC-006**: 100% of create/upload/delete actions on data products and documents produce a corresponding audit record, reconstructible into an ordered action history.
- **SC-007**: 100% of MCP queries against a data product with MCP exposure disabled fail, and 100% succeed (subject to spec 012's authorization) once enabled.

## Assumptions

- Authorization and security hardening for every action in this spec (who may create/rename/delete/upload/enable/disable/expose-via-MCP) is governed by spec 012 (Data Product Authorization Hardening); this spec defines the base capability's existence and behavior, not its access-control rules.
- Ingestion-pipeline reliability — retry, dead-letter recovery, and idempotency for both manual-upload extraction failures and the automated bulk-ingestion pipeline — is governed by spec 013 (Ingestion Pipeline Reliability); this spec's FR-007/FR-013 only require that health/status is tracked and surfaced, not how failures are recovered.
- Sharing with individuals/groups and persona attachment (REQ-DP-6) is governed by spec 018 (Sharing & Permissions Policy) as the single canonical sharing mechanism; this spec does not define its own share-target or role-policy rules.
- Specific rate-limit thresholds (requests per window, per data product vs. per caller) are deployment/tenant-level configuration, not hardcoded by this spec; this spec defines that a limit MUST exist and be enforced, not its numeric value.
- MCP exposure's protocol-level behavior (tool schema, query semantics) reuses the existing MCP integration pattern already established elsewhere in the platform; this spec only adds the per-data-product enable/disable toggle and defers access-boundary status-code behavior to spec 012.
