# Feature Specification: Document Ingestion & Retrieval (RAG)

**Feature Branch**: `005-document-ingestion-retrieval`

**Created**: 2026-07-20

**Status**: Draft

**Input**: Derived from SSD_Document.md §3.2 (Chat Core — document ingestion, retrieval, and PDF export subset only; message-limit/tool-orchestration/model-access material is out of scope, tracked separately), §2's `ChatDocumentModel` entry, and §5's "Incomplete AWS-to-Azure migration" item — reframed from "as-is" discovery findings into target requirements. Source facts: upload → extract (Document Intelligence or plain text) → semantic chunk (~1000 tokens) → batch-embed → batch-write to Cosmos (100 ops/batch) runs asynchronously via Next.js `after()` so the upload response returns immediately; retrieval uses Cosmos-native hybrid search (`RANK RRF` of vector + full-text, 0.7/0.3 weights) falling back to vector-only when no usable keywords exist; PDF export strips `containsCanvasStudentData`-tagged message parts when the exporting user `isStudent`; and (per the sibling Data Products domain, §3.7, which shares the same underlying Document Intelligence extraction layer per §5) extraction failures are swallowed and reported as upload success rather than surfaced accurately.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Upload a document and have it become searchable without blocking the UI (Priority: P1)

A user attaches a file to a chat thread. The upload call returns immediately so the user can keep chatting, while the document is extracted, chunked, embedded, and written to storage in the background, becoming available for retrieval shortly after.

**Why this priority**: This is the foundational capability — without an ingestion pipeline that actually gets content into a searchable form, there is nothing to retrieve. It must exist and behave correctly before any other requirement in this spec is meaningful.

**Independent Test**: Upload a supported document, confirm the upload HTTP response returns before ingestion completes, then poll/query and confirm the document's content becomes retrievable once background processing finishes.

**Acceptance Scenarios**:

1. **Given** a user uploads a supported file (PDF, image, or plain text) to a thread, **When** the upload request is submitted, **Then** the system returns a success response without waiting for extraction, chunking, embedding, or storage to complete.
2. **Given** an uploaded file that Document Intelligence (or plain-text extraction) can successfully process, **When** background ingestion completes, **Then** the extracted content is chunked into roughly 1000-token segments, embedded, and written to storage such that it is retrievable in chat.
3. **Given** a batch of chunks whose count exceeds a single write batch, **When** the batch write executes, **Then** all chunks are written (across as many batches as needed) and none are silently dropped.

---

### User Story 2 - Ingestion status accurately reflects success or failure (Priority: P1)

A user uploads a document that fails to extract (corrupt file, unsupported content, a partial batch-write failure, etc.). The document's ingestion status must show that it failed, not that it succeeded.

**Why this priority**: The sibling Data Products ingestion pipeline (§3.7), which shares the same underlying Document Intelligence extraction layer, is documented as swallowing extraction failures and reporting upload success anyway. A user who believes a failed document is searchable will silently get worse (or no) retrieval results with no signal that anything is wrong — this is a trust/correctness gap at least as severe as the pipeline simply not existing, so it ranks alongside Story 1.

**Independent Test**: Upload a file engineered to fail extraction (e.g., corrupted/unsupported content) and confirm the document's ingestion status reflects failure, distinguishable from a successfully ingested document, in both the API response and any UI surfacing that status.

**Acceptance Scenarios**:

1. **Given** an uploaded file that fails extraction, **When** background ingestion runs, **Then** the document's ingestion status is set to a failure state, never a success state.
2. **Given** an uploaded file that extracts successfully but fails during chunk embedding or batch write, **When** background ingestion runs, **Then** the document's ingestion status reflects that failure rather than reporting success.
3. **Given** a document with a failed ingestion status, **When** a user views their document list, **Then** the failure is visibly distinguishable from successfully ingested documents.
4. **Given** an uploaded file that fully succeeds through extraction, chunking, embedding, and storage, **When** background ingestion completes, **Then** the ingestion status is set to a success state (no false negatives).

---

### User Story 3 - Chat retrieves relevant document content via hybrid search (Priority: P1)

While chatting in a thread with ingested documents, a user asks a question and the system retrieves relevant passages by combining semantic (vector) similarity with full-text keyword matching, degrading gracefully when keyword search isn't useful.

**Why this priority**: Retrieval is the actual value delivered by this feature — ingestion exists only to serve it. It's tied for P1 with Stories 1–2 because a pipeline that ingests documents but retrieves them poorly (or not at all under common query shapes) is just as non-functional from the user's perspective.

**Independent Test**: Query a thread with ingested documents using a question containing usable keywords and confirm hybrid (vector + full-text) ranked results return; repeat with a query containing no usable keywords (e.g., a purely conversational phrase or symbols) and confirm vector-only results still return.

**Acceptance Scenarios**:

1. **Given** a thread with ingested, embedded document chunks, **When** a user's query contains usable keywords, **Then** the system returns results ranked via RRF fusion of vector similarity and full-text search (weighted 0.7 vector / 0.3 full-text by default).
2. **Given** a thread with ingested document chunks, **When** a user's query contains no usable keywords for full-text search, **Then** the system falls back to vector-only retrieval and still returns results rather than failing or returning nothing.
3. **Given** a thread with no ingested (or only failed) documents, **When** a user issues a query, **Then** retrieval returns no document results without erroring the chat request.

---

### User Story 4 - Canvas-student-tagged content is stripped from student PDF exports (Priority: P2)

A Canvas-launched student exports a chat thread to PDF. Any message part tagged as containing Canvas student data must not appear in the rendered PDF.

**Why this priority**: This is a narrower, privacy-relevant control scoped to one export path, already working as intended per the discovery findings — it's included here to lock in as a regression-tested requirement, but it doesn't gate the core ingestion/retrieval value delivered by Stories 1–3.

**Independent Test**: Export a thread containing a mix of tagged and untagged message parts as a student user, and confirm only the tagged parts are absent from the resulting PDF; repeat as a non-student user and confirm tagged parts remain present.

**Acceptance Scenarios**:

1. **Given** a thread with a message part tagged `containsCanvasStudentData`, **When** a user with `isStudent:true` exports the thread to PDF, **Then** that part is stripped from the rendered output.
2. **Given** the same thread, **When** a user without `isStudent:true` exports the thread to PDF, **Then** tagged parts are rendered normally (the stripping rule does not over-apply).
3. **Given** a message with both tagged and untagged parts, **When** a student exports it, **Then** only the tagged parts are removed and the untagged parts still render.

### Edge Cases

- What happens when a document fails extraction entirely (e.g., unreadable/corrupt file, unsupported format slipping past the extension allow-list)? Status must reflect failure, per Story 2.
- What happens when extraction succeeds but the embedding or batch-write stage fails partway (given Cosmos batch writes are capped at 100 ops per partition and are not a true atomic transaction)? Partial success must not be reported as full success.
- How does the system behave when a chat query arrives for a thread whose documents are still mid-ingestion (upload accepted, background processing not yet complete)?
- How does hybrid search behave when a query contains only symbols/stop-words with no indexable keywords, versus a query that is empty?
- What happens to retrieval when a document's ingestion status is failed — must it be excluded from search results rather than surfacing empty/corrupt chunks?
- How does PDF export behave for a message part that is tagged `containsCanvasStudentData` but the exporting user's role flags are ambiguous (e.g., mid-impersonation)? Impersonated sessions must be evaluated using the effective (post-downgrade) role, consistent with role-derivation behavior elsewhere in the system.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST return a successful upload response immediately upon accepting a valid file, without waiting for extraction, chunking, embedding, or storage to complete.
- **FR-002**: The system MUST extract text content from uploaded files using Document Intelligence for non-plain-text formats, or direct text extraction for plain-text files.
- **FR-003**: The system MUST split extracted text into semantic chunks of approximately 1000 tokens before embedding.
- **FR-004**: The system MUST generate embeddings for all chunks and write them to storage in batches (up to 100 operations per batch), ensuring all chunks are written even when the total exceeds one batch.
- **FR-005**: The system MUST set a document's ingestion status to a failure state whenever any pipeline stage (extraction, chunking, embedding, or storage write) does not complete successfully for that document — it MUST NOT report a success status when any stage failed.
- **FR-006**: The system MUST set a document's ingestion status to a success state only when extraction, chunking, embedding, and storage all complete successfully.
- **FR-007**: The system MUST make a document's current ingestion status (success, failure, or in-progress) visible to the user who owns/uploaded it.
- **FR-008**: Chat retrieval MUST query only successfully-ingested document chunks; documents in a failed or in-progress ingestion state MUST be excluded from search results.
- **FR-009**: Chat retrieval MUST use hybrid search combining vector similarity and full-text search, fused via RRF ranking, weighted 0.7 vector / 0.3 full-text by default.
- **FR-010**: WHEN a query contains no usable keywords for full-text search, THE SYSTEM MUST fall back to vector-only retrieval rather than returning an error or no results.
- **FR-011**: WHEN a user whose effective role is `isStudent` exports a chat thread to PDF, THE SYSTEM MUST strip any message part tagged `containsCanvasStudentData` before rendering.
- **FR-012**: PDF export stripping MUST apply per-part, not per-message — untagged parts within a message that also contains a tagged part MUST still render.
- **FR-013**: PDF export MUST NOT strip `containsCanvasStudentData`-tagged parts for exporting users whose effective role is not `isStudent`.

### Key Entities *(include if feature involves data)*

- **ChatDocumentModel**: uploaded-file metadata scoped to a thread — `id`, `threadId`, `fileName`, `ingestionStatus` (must resolve, for this pipeline, to one of: in-progress, succeeded, or failed — the broader multi-state enum's AWS-pipeline-vestige values are out of scope, see Assumptions).
- **Document chunk**: a semantically-chunked (~1000 token) segment of a document's extracted text, paired with its embedding vector, stored for hybrid retrieval.
- **ChatMessageModelV2 / message part**: an individual part of a stored chat message; may carry the `containsCanvasStudentData` tag consumed by PDF export.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Upload requests return a response in a time independent of document size or ingestion duration (no user-perceived blocking), across a test corpus of small and large files.
- **SC-002**: 100% of documents that fully succeed through extraction, chunking, embedding, and storage report a success ingestion status; 100% of documents that fail at any stage report a failure status — 0% of failures are misreported as success, across a test corpus of valid and intentionally-broken files.
- **SC-003**: 100% of chat queries containing usable keywords return RRF-fused hybrid results; 100% of queries with no usable keywords still return vector-only results — 0% of queries fail outright due to retrieval.
- **SC-004**: 0% of failed or in-progress documents' chunks appear in retrieval results across a test corpus mixing successful, failed, and in-progress documents in the same thread.
- **SC-005**: 100% of `containsCanvasStudentData`-tagged message parts are absent from PDF exports performed by `isStudent` users, and 100% of untagged parts in the same messages still render, across a test corpus of mixed-tag threads.

## Assumptions

- Document Intelligence remains the extraction backend for non-plain-text files; this spec does not change the extraction engine or introduce a new one, only the fidelity of status reporting around it.
- The two duplicate document-text-extraction abstraction layers noted in SSD_Document.md §5 (both wrapping the same underlying Document Intelligence client) are a separate consolidation concern; the requirements here apply regardless of which abstraction layer is in place at implementation time.
- Dead AWS-era fields on `ChatDocumentModel` (`s3ObjectKey`, `bedrockDocumentId`, `deletionMessageId`, `lambdaProcessing`) and the unreachable AWS-pipeline `ingestionStatus` enum values are out of scope for removal in this spec; they must simply not interfere with the accurate in-progress/succeeded/failed reporting required above.
- The extraction-failure-swallowing defect is documented explicitly for the sibling Data Products ingestion pipeline (§3.7); since Chat Core's document ingestion shares the same underlying Document Intelligence extraction layer (§5) and its `ingestionStatus` enum is described as not fully modeling the current Cosmos-only pipeline's real outcomes, this spec treats the same defect pattern as present here and requires it fixed for Chat Core specifically. The Data Products pipeline's own fix is out of scope, tracked separately.
- The 0.7/0.3 vector/full-text RRF weighting is retained as the current default; tuning these weights is out of scope for this spec.
- The 100-ops-per-batch Cosmos write cap and its non-atomic nature (per `IDatabaseProvider`) are existing infrastructure constraints this spec's status-accuracy requirement must account for, not eliminate.
- Message-limit enforcement, tool orchestration, and model-access-control material from SSD_Document.md §3.2 are excluded from this spec and tracked as a separate feature.
