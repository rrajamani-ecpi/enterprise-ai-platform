# Feature Specification: Data Product Ingestion Pipeline Reliability

**Feature Branch**: `013-ingestion-pipeline-reliability`

**Created**: 2026-07-20

**Status**: Draft

**Input**: Derived from SSD_Document.md §3.7 (Data Products) and §5 (Architectural Debt) — reframed from "as-is" discovery findings into target requirements. Source facts: the Azure Logic Apps ingestion pipeline (Delta Walker polling Microsoft Graph for changes, a Job Processor that extracts/chunks/embeds and writes vector rows, a daily Cleanup job that soft-deletes stale rows, and a feature-flagged HTTP-triggered Blob Backfill job) runs autonomously on Azure-internal schedules with no in-repo TypeScript code ever invoking it; the documented retry/dead-letter mechanism (`retryCount`, `deadletters` container) is fully specified in the schema/docs but never actually written to by any workflow, so failed ingestion jobs are effectively dropped with no recovery path; and file upload extraction failures during upload are swallowed while the upload still reports success to the user.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Failed ingestion jobs are recoverable via retry and dead-letter (Priority: P1)

An ingestion job (Delta Walker sync, Job Processor extraction/chunk/embed, or Blob Backfill run) fails partway through — e.g., a transient Microsoft Graph error, Document Intelligence timeout, or embedding-call failure — and today that failure is silently dropped: `retryCount` is never incremented and no record is ever written to the `deadletters` container, so no operator or automated process can find or recover it.

**Why this priority**: This is the single largest reliability gap in the pipeline. A recovery mechanism (`retryCount`, `deadletters`) is fully specified in the schema and documentation but has zero implementing code — every failed job is a silent, permanent data-ingestion loss with no visibility. Nothing else in this spec matters as much: even a perfectly correct happy-path pipeline is unreliable if failures vanish without a trace.

**Independent Test**: Force an ingestion job to fail (e.g., inject a downstream error during extraction or embedding), let it exhaust its retry attempts, and confirm — purely by inspecting Cosmos container state — that `retryCount` incremented on each attempt and a corresponding record exists in the `deadletters` container describing the failed job and its cause.

**Acceptance Scenarios**:

1. **Given** an ingestion job that fails on its first attempt due to a transient error, **When** the workflow retries, **Then** the job's `retryCount` field is incremented and persisted, observable in the source document/container.
2. **Given** an ingestion job that continues to fail after exhausting the configured maximum retry attempts, **When** the final attempt fails, **Then** a record is written to the `deadletters` container containing enough detail (job type, target document/URL, failure reason, timestamp, final `retryCount`) to diagnose and manually reprocess it.
3. **Given** a job that fails once but succeeds on retry, **When** the retry succeeds, **Then** no dead-letter record is created and the document's `ingestionStatus` reflects successful completion.

---

### User Story 2 - Upload-time ingestion status accurately reflects extraction failure (Priority: P1)

A user uploads a file to a data product; the extraction step (Document Intelligence or plain-text extraction) fails, but today that failure is swallowed internally and the upload still reports success to the user — leaving them to believe the document was ingested when no usable content was ever produced.

**Why this priority**: This is a user-facing correctness bug with direct trust and data-quality impact: a data product owner has no way to know retrieval will silently miss a document they were told succeeded. It's ranked alongside Story 1 as a P1 because both are about the same class of problem — failures that vanish instead of surfacing — just at different points in the pipeline (synchronous upload vs. asynchronous scheduled jobs).

**Independent Test**: Upload a file to a data product that is guaranteed to fail extraction (e.g., a corrupted or unsupported-format file), and confirm the upload response and the persisted `ingestionStatus` both reflect failure — not success — without requiring any change to the underlying extraction dependency.

**Acceptance Scenarios**:

1. **Given** a file whose extraction step fails, **When** the upload completes, **Then** the returned upload result does not report success, and the document's persisted `ingestionStatus` is set to a failure state rather than a completed/success state.
2. **Given** a file that extracts successfully, **When** the upload completes, **Then** the upload result reports success and `ingestionStatus` reflects successful completion, unchanged from current correct behavior.
3. **Given** a data product's document list after a failed upload, **When** the owner views it, **Then** the failed document is visibly distinguishable from successfully ingested documents (not indistinguishable "success" entries).

---

### User Story 3 - Scheduled pipeline components produce observable, verifiable outcomes (Priority: P3)

The Delta Walker (polls Microsoft Graph for source changes on a recurrence schedule), the Job Processor (extracts, chunks, embeds, and writes vector rows), the daily Cleanup job (soft-deletes stale rows), and the feature-flagged Blob Backfill job (HTTP-triggered, on-demand reprocessing) all run autonomously on Azure-internal schedules with no in-repo trigger. This is existing, correct behavior — but there is currently no way to verify from outside Azure that any of them ran, succeeded, or produced the expected effect.

**Why this priority**: These components already work as designed and are not bugs to fix — this story exists to formalize what "the pipeline is healthy" means in terms an engineer or QA process can check without access to the Logic Apps runtime, so that Stories 1 and 2's recovery/status guarantees have something concrete to attach to. It's ranked P3 because it documents/verifies existing behavior rather than fixing a gap.

**Independent Test**: Without triggering anything from application code, observe Cosmos container state over one full schedule cycle and confirm: the Delta Walker's change cursor advances, new/changed source items produce corresponding vector rows written by the Job Processor, rows past the staleness threshold are soft-deleted by the Cleanup job, and — with the Blob Backfill feature flag enabled — an HTTP-triggered backfill run produces the same vector-row outcome as normal ingestion.

**Acceptance Scenarios**:

1. **Given** a source item changed in Microsoft Graph since the last Delta Walker run, **When** the next scheduled run occurs, **Then** the change is reflected as new/updated vector rows for the corresponding data product, observable in Cosmos.
2. **Given** vector rows older than the Cleanup job's staleness threshold, **When** the daily Cleanup job runs, **Then** those rows are soft-deleted (not hard-deleted), consistent with the rest of the system's soft-delete convention.
3. **Given** the Blob Backfill feature flag is enabled and a backfill is triggered via its HTTP endpoint, **When** the run completes, **Then** the targeted blobs produce the same extraction/chunk/embed/vector-row outcome as the normal Job Processor path.
4. **Given** the Blob Backfill feature flag is disabled, **When** its HTTP endpoint is invoked, **Then** the run does not execute.

### Edge Cases

- What happens when a job fails after partially writing vector rows (e.g., some chunks embedded and written, then a later batch fails)? Recovery must not duplicate already-written rows on retry, and a resulting dead-letter record must reflect the partial state.
- How does the system behave if the Delta Walker and Job Processor overlap (a new Graph change arrives for a document the Job Processor is still processing from a prior run)?
- What happens when the Cleanup job runs concurrently with an in-flight Job Processor run against the same data product's rows?
- How does a dead-letter record surface for a document whose parent data product was deleted (soft-delete) between job start and failure?
- What happens when extraction fails for a reason that is itself transient (e.g., Document Intelligence rate-limited) versus permanent (e.g., unsupported file format) — should both retry, or should permanent failures dead-letter immediately without exhausting retries?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every ingestion job attempt (Delta Walker sync, Job Processor run, Blob Backfill run) MUST increment and persist a `retryCount` on failure before any retry occurs.
- **FR-002**: The system MUST write a record to the `deadletters` container when an ingestion job exhausts its configured maximum retry attempts, containing sufficient detail (job type, target identifier, failure reason, timestamp, final `retryCount`) to diagnose and manually reprocess the failure.
- **FR-003**: A job that succeeds on retry MUST NOT produce a dead-letter record, and its target document's `ingestionStatus` MUST reflect successful completion.
- **FR-004**: File upload extraction failures MUST NOT be swallowed — the upload result and the persisted `ingestionStatus` MUST both reflect failure when extraction fails.
- **FR-005**: A document's `ingestionStatus` MUST be visibly distinguishable between success and failure states in any UI or API surface that lists a data product's documents.
- **FR-006**: The Delta Walker, Job Processor, Cleanup job, and Blob Backfill job MUST continue to run on their existing autonomous schedules/triggers, independent of any in-repo TypeScript invocation.
- **FR-007**: The Cleanup job's row removal MUST remain a soft delete, consistent with the system's existing soft-delete convention.
- **FR-008**: The Blob Backfill job MUST remain gated behind its feature flag and MUST NOT execute when the flag is disabled.
- **FR-009**: Retry/dead-letter handling MUST be idempotent with respect to partial prior writes — a retried or backfilled job MUST NOT create duplicate vector rows for chunks already successfully written.

### Key Entities *(include if feature involves data)*

- **DataProductDocumentModel**: ingested source metadata including `ingestionStatus` (must accurately reflect success vs. failure, including extraction failures at upload time).
- **Ingestion Job Record**: represents one attempt of a Delta Walker, Job Processor, or Blob Backfill run; carries `retryCount` (currently schema'd but never written).
- **Dead-letter Record** (`deadletters` container): a failure record created when an ingestion job exhausts retries; must capture job type, target identifier, failure reason, timestamp, and final `retryCount`.
- **Vector Row**: the chunked/embedded content written by the Job Processor for retrieval; subject to soft-delete by the Cleanup job when stale.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of ingestion jobs that fail and exhaust retries produce a corresponding `deadletters` record, verified across Delta Walker, Job Processor, and Blob Backfill failure scenarios.
- **SC-002**: 100% of jobs that fail at least once but eventually succeed show correct `retryCount` progression and zero dead-letter records.
- **SC-003**: 100% of file uploads with a failing extraction step report failure (not success) in both the upload response and persisted `ingestionStatus`, across a test corpus of corrupted/unsupported-format files.
- **SC-004**: 0 duplicate vector rows are created when a previously partially-completed job is retried or backfilled.
- **SC-005**: Over one full scheduled cycle, 100% of changed Microsoft Graph source items produce corresponding vector-row updates, and 100% of rows past the staleness threshold are soft-deleted by the Cleanup job — both verifiable from Cosmos container state alone.

## Assumptions

- The Logic Apps workflow definitions themselves (`workflow.json` and related Azure-native configuration) are out of scope for this spec's testing, since they live outside the TypeScript codebase; "done" is defined in terms of observable data-plane outcomes (dead-letter records appearing, `retryCount` incrementing, vector rows being written/soft-deleted) rather than workflow implementation details.
- The Delta Walker's Microsoft Graph polling cadence, the Job Processor's extraction/chunking/embedding approach, and the Cleanup job's staleness threshold are retained as-is; this spec adds reliability/observability guarantees around them, not a redesign of their core logic.
- The Blob Backfill job's feature-flag gating mechanism is retained as-is; this spec only requires that flag-off behavior remains a strict no-op.
- CRUD, authorization, and the external MCP endpoint surfaces for Data Products (schema validation, sharing/visibility, API-key-gated MCP responses) are covered by a separate spec and are explicitly out of scope here.
- "Exhausts retries" assumes a maximum retry count is configured for each job type; this spec does not mandate a specific numeric threshold, only that one exists and is enforced consistently with dead-letter creation.
