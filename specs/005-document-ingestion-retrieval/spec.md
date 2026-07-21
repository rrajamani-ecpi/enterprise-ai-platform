# Feature Specification: Document Ingestion & Retrieval (RAG)

**Feature Branch**: `005-document-ingestion-retrieval`

**Created**: 2026-07-20

**Status**: Draft

**Input**: Derived from SSD_Document.md §3.2 (Chat Core — document ingestion, retrieval, and PDF export subset only; message-limit/tool-orchestration/model-access material is out of scope, tracked separately), §2's `ChatDocumentModel` entry, and §5's "Incomplete AWS-to-Azure migration" item — reframed from "as-is" discovery findings into target requirements — merged with `docs/PRODUCT_REQUIREMENTS_DOCUMENT.md` §4.5 (Retrieval-Augmented Generation (RAG) & Knowledge Grounding), the primary forward-looking source, which adds explicit chunk-overlap configurability and dual retrieval-scope (per-chat "Document Chat" vs. Data Products) as first-class requirements, plus configurable top-K and citation production not present in the as-is discovery; and further merged with §4.22 (File Upload & Processing), which adds upload-time file type/size validation with object storage of originals (REQ-FILE-1), actionable handling of password-protected/corrupt uploads (REQ-FILE-2), and confirms/extends unified deletion across chat and Data Product scopes (REQ-FILE-3). Source facts: upload → extract (Document Intelligence or plain text) → semantic chunk (~1000 tokens) → batch-embed → batch-write to Cosmos (100 ops/batch) runs asynchronously via Next.js `after()` so the upload response returns immediately; retrieval uses Cosmos-native hybrid search (`RANK RRF` of vector + full-text, 0.7/0.3 weights) falling back to vector-only when no usable keywords exist; PDF export strips `containsCanvasStudentData`-tagged message parts when the exporting user `isStudent`; and (per the sibling Data Products domain, §3.7, which shares the same underlying Document Intelligence extraction layer per §5) extraction failures are swallowed and reported as upload success rather than surfaced accurately.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Upload a document and have it become searchable without blocking the UI (Priority: P1)

A user attaches a file to a chat thread. The upload call returns immediately so the user can keep chatting, while the document is extracted, chunked, embedded, and written to storage in the background, becoming available for retrieval shortly after.

**Why this priority**: This is the foundational capability — without an ingestion pipeline that actually gets content into a searchable form, there is nothing to retrieve. It must exist and behave correctly before any other requirement in this spec is meaningful.

**Independent Test**: Upload a supported document, confirm the upload HTTP response returns before ingestion completes, then poll/query and confirm the document's content becomes retrievable once background processing finishes.

**Acceptance Scenarios**:

1. **Given** a user uploads a supported file (PDF, image, or plain text) to a thread, **When** the upload request is submitted, **Then** the system returns a success response without waiting for extraction, chunking, embedding, or storage to complete.
2. **Given** an uploaded file that Document Intelligence (or plain-text extraction) can successfully process, **When** background ingestion completes, **Then** the extracted content is chunked into roughly 1000-token segments, embedded, and written to storage such that it is retrievable in chat.
3. **Given** a batch of chunks whose count exceeds a single write batch, **When** the batch write executes, **Then** all chunks are written (across as many batches as needed) and none are silently dropped.
4. **Given** a file whose type is not in the supported allow-list or whose size exceeds the configured maximum, **When** the upload request is submitted, **Then** the system rejects the upload with an actionable validation error before any extraction, chunking, embedding, or storage begins, and no ingestion record is created.
5. **Given** a file that passes type/size validation, **When** the upload is accepted, **Then** the original file is stored in object storage prior to (or concurrently with) background extraction beginning.

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
5. **Given** an uploaded file that is password-protected or otherwise corrupt/unreadable such that extraction cannot proceed, **When** background ingestion attempts extraction, **Then** the failure status is accompanied by an actionable, cause-specific error message (e.g., distinguishing "password-protected" from "corrupt/unreadable") rather than a generic failure indicator.

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

---

### User Story 5 - Retrieval is correctly scoped to per-chat documents vs. data-product collections (Priority: P2)

A user queries a chat thread that has both documents attached directly to that thread ("Document Chat") and one or more Data Products attached to the active persona or invoked as a tool. Retrieval must keep these two scopes separate: per-chat document results come only from the current thread's own uploads, and Data Product results come only from the specific collection(s) in play, never bleeding into each other or into unrelated threads/collections.

**Why this priority**: Per PRD §4.5, per-chat documents and Data Products are two distinct, first-class retrieval scopes (partitioned by thread vs. by collection ID). Cross-scope leakage would surface another user's or another collection's content in the wrong context — a correctness and data-boundary issue — but it is ranked P2 rather than P1 because the Data Product side of this scope depends on Data Product CRUD/authorization (specs 012/013) existing first; this story defines the retrieval-time partitioning contract, not Data Product management itself.

**Independent Test**: Ingest documents into thread A and a separate document into thread B, plus documents into Data Product collection X; query from thread A and confirm only thread A's per-chat chunks are eligible, then query with collection X attached and confirm only collection X's chunks are eligible for that portion of results.

**Acceptance Scenarios**:

1. **Given** per-chat documents ingested into thread A and different per-chat documents ingested into thread B, **When** a user queries from thread A, **Then** only thread A's document chunks are eligible for retrieval — thread B's chunks are excluded regardless of semantic similarity.
2. **Given** a Data Product collection attached to a persona or invoked as a tool, **When** a query is issued against it, **Then** only chunks partitioned under that collection's ID are eligible for retrieval.
3. **Given** a chat thread with both its own per-chat documents and an attached Data Product collection, **When** a user queries, **Then** results are drawn from each scope independently (per-chat chunks scoped to the thread, Data Product chunks scoped to the collection), without one scope's partition boundary leaking into the other.

---

### User Story 6 - Retrieved answers carry citations back to source documents (Priority: P2)

After hybrid retrieval returns ranked chunks, the system formats them into a context block with source attribution and produces citations so the user can see which document(s) an answer's content came from.

**Why this priority**: Per PRD §4.5, retrieval isn't just about finding relevant chunks — it must also produce citations linking answer content to source documents. Without this, retrieval results are unverifiable to the user. It's P2 because it builds on Story 3's hybrid retrieval (must exist first) and is a trust/traceability layer on top of it rather than the core retrieval mechanism itself.

**Independent Test**: Issue a query that retrieves chunks from two different source documents, confirm the resulting context block attributes each chunk to its source, and confirm the persisted answer carries citation records pointing back to those source documents (and, for a query that retrieves no chunks, confirm no citations are fabricated).

**Acceptance Scenarios**:

1. **Given** hybrid retrieval returns one or more ranked chunks, **When** the results are formatted for the model, **Then** each chunk is attributed to its source document in the resulting context block.
2. **Given** an answer generated using retrieved chunks, **When** the answer is persisted, **Then** citations linking the answer content to its source document(s) are produced and persisted alongside it.
3. **Given** a query for which retrieval returns no chunks, **When** the answer is generated, **Then** no citations are fabricated or attached.

---

### User Story 7 - Deleting a document removes its derived chunks and vectors (Priority: P3)

A user or data-product owner deletes an uploaded document. Its derived chunks and embedding vectors must no longer be retrievable, and the deletion itself must be tracked so the user can tell it succeeded rather than silently leaving stale content queryable.

**Why this priority**: Per PRD §4.5/§4.22 (REQ-FILE-3, unified deletion across chat and data-product scopes), deletion is a necessary lifecycle counterpart to ingestion, but it's lower priority than the ingestion/retrieval/citation stories because a document that's merely never deleted doesn't break the ingest-and-retrieve happy path — it only risks stale/orphaned content over time.

**Independent Test**: Ingest a document, confirm its chunks are retrievable, delete the document, and confirm both that its chunks no longer appear in retrieval results and that the document's deletion status reflects completion (not left in an ambiguous or silently-failed state).

**Acceptance Scenarios**:

1. **Given** a successfully ingested per-chat document, **When** the user deletes it, **Then** its derived chunks and vectors are removed (or excluded) from future retrieval and the document's deletion status reflects completion.
2. **Given** a successfully ingested Data Product document, **When** its owner deletes it, **Then** its derived chunks and vectors are removed (or excluded) from future retrieval consistent with the Data Product pipeline's existing soft-delete convention (see spec 013's Cleanup job), and the deletion status reflects completion.
3. **Given** a deletion that fails partway (e.g., chunk/vector removal fails after the document record is marked deleted), **When** the failure occurs, **Then** the deletion status reflects the failure rather than falsely reporting completion.

### Edge Cases

- What happens when a document fails extraction entirely (e.g., unreadable/corrupt file, unsupported format slipping past the extension allow-list)? Status must reflect failure, per Story 2.
- What happens when an uploaded file exceeds the configured maximum size, or its type is not in the supported allow-list? The upload must be rejected with an actionable error before ingestion begins, per Story 1 and FR-020 — it must not be accepted and then silently fail later in the pipeline.
- What happens when a file is password-protected rather than merely corrupt? The failure message must distinguish this cause from generic corruption so the user can take an appropriate action (e.g., re-upload without a password), per FR-021.
- What happens when extraction succeeds but the embedding or batch-write stage fails partway (given Cosmos batch writes are capped at 100 ops per partition and are not a true atomic transaction)? Partial success must not be reported as full success.
- How does the system behave when a chat query arrives for a thread whose documents are still mid-ingestion (upload accepted, background processing not yet complete)?
- How does hybrid search behave when a query contains only symbols/stop-words with no indexable keywords, versus a query that is empty?
- What happens to retrieval when a document's ingestion status is failed — must it be excluded from search results rather than surfacing empty/corrupt chunks?
- How does PDF export behave for a message part that is tagged `containsCanvasStudentData` but the exporting user's role flags are ambiguous (e.g., mid-impersonation)? Impersonated sessions must be evaluated using the effective (post-downgrade) role, consistent with role-derivation behavior elsewhere in the system.
- What happens when a chunk boundary would fall mid-word or mid-sentence given the configured chunk size/overlap? The boundary must shift to the nearest word/sentence break rather than splitting mid-token.
- What happens when a query is issued against a Data Product collection the requesting user/persona is not authorized to access? Authorization (spec 012) is evaluated before retrieval partitioning; unauthorized collections must not be queried.
- How does citation production behave when the same source document contributes multiple non-contiguous chunks to one answer? Citations must not duplicate the same source document redundantly beyond what's needed to attribute distinct passages.
- What happens when a document is deleted while it is still mid-ingestion (chunks/vectors partially written)? Deletion must account for partial ingestion state and not leave orphaned chunks from an incomplete run.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST return a successful upload response immediately upon accepting a valid file, without waiting for extraction, chunking, embedding, or storage to complete.
- **FR-002**: The system MUST extract text content from uploaded files using Document Intelligence for non-plain-text formats, or direct text extraction for plain-text files.
- **FR-003**: The system MUST split extracted text into chunks of a configurable size (default ~1000 characters/tokens) with a configurable overlap between consecutive chunks (default ~200 characters), preserving word and sentence boundaries (never splitting mid-word) before embedding.
- **FR-004**: The system MUST generate embeddings (1536-dimension vectors) for all chunks and write them, together with source metadata (at minimum: source document identifier and retrieval-scope partition — thread ID or collection ID), to a vector-searchable store in batches (up to 100 operations per batch), ensuring all chunks are written even when the total exceeds one batch.
- **FR-005**: The system MUST set a document's ingestion status to a failure state whenever any pipeline stage (extraction, chunking, embedding, or storage write) does not complete successfully for that document — it MUST NOT report a success status when any stage failed.
- **FR-006**: The system MUST set a document's ingestion status to a success state only when extraction, chunking, embedding, and storage all complete successfully.
- **FR-007**: The system MUST make a document's current ingestion status (success, failure, or in-progress) visible to the user who owns/uploaded it, and to admins with visibility into the owning thread/collection. (Retry-attempt and dead-letter tracking for the underlying ingestion jobs is specified in spec 013 for the Data Products pipeline; this spec's per-chat `ChatDocumentModel` status model remains the simpler in-progress/succeeded/failed set per the Assumptions below.)
- **FR-008**: Chat retrieval MUST query only successfully-ingested document chunks; documents in a failed or in-progress ingestion state MUST be excluded from search results.
- **FR-009**: Chat retrieval MUST use hybrid search combining vector similarity and full-text search, fused via RRF ranking, weighted 0.7 vector / 0.3 full-text by default.
- **FR-010**: WHEN a query contains no usable keywords for full-text search, THE SYSTEM MUST fall back to vector-only retrieval rather than returning an error or no results.
- **FR-011**: WHEN a user whose effective role is `isStudent` exports a chat thread to PDF, THE SYSTEM MUST strip any message part tagged `containsCanvasStudentData` before rendering.
- **FR-012**: PDF export stripping MUST apply per-part, not per-message — untagged parts within a message that also contains a tagged part MUST still render.
- **FR-013**: PDF export MUST NOT strip `containsCanvasStudentData`-tagged parts for exporting users whose effective role is not `isStudent`.
- **FR-014**: Hybrid retrieval MUST return a configurable top-K number of ranked results, defaulting to ~100 for per-chat retrieval and ~20 for Data Product retrieval.
- **FR-015**: Per-chat document retrieval MUST be partitioned so that only chunks belonging to the querying thread are eligible for that thread's results.
- **FR-016**: Data Product retrieval MUST be partitioned by collection ID so that only chunks belonging to the collection(s) attached to the current persona/tool invocation are eligible for results.
- **FR-017**: The system MUST format retrieved chunks into a context block with source attribution before passing them to the model.
- **FR-018**: The system MUST produce and persist citations linking generated answer content to the source document(s) the retrieved chunks came from.
- **FR-019**: The system MUST support deleting a document such that its derived chunks and vectors are removed from (or excluded from) future retrieval, tracking a deletion status that reflects success or failure of the deletion rather than assuming success. For Data Products, derived-row removal follows the same soft-delete convention as spec 013's Cleanup job rather than introducing a new deletion mechanism.
- **FR-020**: The system MUST validate an uploaded file's type (against a supported allow-list) and size (against a configured maximum) before accepting it; files failing validation MUST be rejected with an actionable error and MUST NOT proceed to extraction, chunking, embedding, or storage. Files that pass validation MUST have their original bytes stored in object storage.
- **FR-021**: WHEN extraction fails because a file is password-protected or otherwise corrupt/unreadable, THE SYSTEM MUST report the ingestion failure with a cause-specific, actionable error message (distinguishing at minimum "password-protected" from "corrupt/unreadable") rather than a generic failure indicator, consistent with the failure-status handling in FR-005.

### Key Entities *(include if feature involves data)*

- **ChatDocumentModel**: uploaded-file metadata scoped to a thread — `id`, `threadId`, `fileName`, `ingestionStatus` (must resolve, for this pipeline, to one of: in-progress, succeeded, or failed — the broader multi-state enum's AWS-pipeline-vestige values are out of scope, see Assumptions), a pointer to the original file's object-storage location, and — on failure — a cause-specific error message (e.g., validation-rejected, password-protected, corrupt/unreadable) per FR-020/FR-021.
- **Document chunk**: a chunked (configurable size/overlap, default ~1000 characters/tokens with ~200 overlap) segment of a document's extracted text, paired with its embedding vector (1536 dimensions) and source metadata, stored for hybrid retrieval, partitioned by thread ID (per-chat) or collection ID (Data Products).
- **ChatMessageModelV2 / message part**: an individual part of a stored chat message; may carry the `containsCanvasStudentData` tag consumed by PDF export.
- **Data Product collection**: a curated, reusable document collection queryable as a retrieval scope distinct from per-chat documents; ownership, CRUD, versioning, and sharing/authorization are owned by specs 012/013 — this spec only defines its retrieval-time partitioning (FR-016) and shared deletion convention (FR-019).
- **Citation**: a persisted link between generated answer content and the source document(s)/chunk(s) it was retrieved from, produced whenever hybrid retrieval returns results used to generate an answer.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Upload requests return a response in a time independent of document size or ingestion duration (no user-perceived blocking), across a test corpus of small and large files.
- **SC-002**: 100% of documents that fully succeed through extraction, chunking, embedding, and storage report a success ingestion status; 100% of documents that fail at any stage report a failure status — 0% of failures are misreported as success, across a test corpus of valid and intentionally-broken files.
- **SC-003**: 100% of chat queries containing usable keywords return RRF-fused hybrid results; 100% of queries with no usable keywords still return vector-only results — 0% of queries fail outright due to retrieval.
- **SC-004**: 0% of failed or in-progress documents' chunks appear in retrieval results across a test corpus mixing successful, failed, and in-progress documents in the same thread.
- **SC-005**: 100% of `containsCanvasStudentData`-tagged message parts are absent from PDF exports performed by `isStudent` users, and 100% of untagged parts in the same messages still render, across a test corpus of mixed-tag threads.
- **SC-006**: 100% of retrieval requests return no more than the configured top-K result count (default ~100 chat / ~20 Data Product), verified across both retrieval scopes.
- **SC-007**: 0% of retrieval results cross a partition boundary — no thread's query returns another thread's per-chat chunks, and no Data Product query returns another collection's chunks, across a test corpus of multiple threads and collections.
- **SC-008**: 100% of answers generated from retrieved chunks carry at least one citation to a source document, and 0% of answers generated with no retrieved chunks carry a fabricated citation.
- **SC-009**: 100% of deleted documents have 0% of their chunks/vectors appear in subsequent retrieval results, and 100% of deletions (success or failure) report an accurate, non-default-assumed deletion status.
- **SC-010**: 100% of uploads with a disallowed file type or size over the configured maximum are rejected before any extraction/chunking/embedding/storage occurs, across a test corpus of valid, oversized, and disallowed-type files; 100% of validated files have their original bytes present in object storage.
- **SC-011**: 100% of ingestion failures caused by password-protected or corrupt/unreadable files carry a cause-specific, actionable error message (not a generic failure code), across a test corpus of password-protected and corrupt files.

## Assumptions

- Document Intelligence remains the extraction backend for non-plain-text files; this spec does not change the extraction engine or introduce a new one, only the fidelity of status reporting around it.
- The two duplicate document-text-extraction abstraction layers noted in SSD_Document.md §5 (both wrapping the same underlying Document Intelligence client) are a separate consolidation concern; the requirements here apply regardless of which abstraction layer is in place at implementation time.
- Dead AWS-era fields on `ChatDocumentModel` (`s3ObjectKey`, `bedrockDocumentId`, `deletionMessageId`, `lambdaProcessing`) and the unreachable AWS-pipeline `ingestionStatus` enum values are out of scope for removal in this spec; they must simply not interfere with the accurate in-progress/succeeded/failed reporting required above.
- The extraction-failure-swallowing defect is documented explicitly for the sibling Data Products ingestion pipeline (§3.7); since Chat Core's document ingestion shares the same underlying Document Intelligence extraction layer (§5) and its `ingestionStatus` enum is described as not fully modeling the current Cosmos-only pipeline's real outcomes, this spec treats the same defect pattern as present here and requires it fixed for Chat Core specifically. The Data Products pipeline's own fix is out of scope, tracked separately.
- The 0.7/0.3 vector/full-text RRF weighting is retained as the current default; tuning these weights is out of scope for this spec.
- The 100-ops-per-batch Cosmos write cap and its non-atomic nature (per `IDatabaseProvider`) are existing infrastructure constraints this spec's status-accuracy requirement must account for, not eliminate.
- Message-limit enforcement, tool orchestration, and model-access-control material from SSD_Document.md §3.2 are excluded from this spec and tracked as a separate feature.
- The as-is ~1000-token chunk size (SSD_Document.md §3.2) and PRD §4.5's ~1000-character/~200-character-overlap default are treated as the same configurable chunk-size/overlap parameter; this spec does not mandate a specific unit (tokens vs. characters), only that size and overlap are configurable with sensible defaults and natural-boundary preservation.
- Retry/dead-letter mechanics for ingestion jobs (`retryCount`, `deadletters` container) are specified in spec 013 (Data Product Ingestion Pipeline Reliability) and are not duplicated here; this spec's FR-007/FR-019 reference that mechanism rather than re-defining it. Per-chat `ChatDocumentModel` ingestion retains the simpler in-progress/succeeded/failed model noted above.
- Data Product CRUD, versioning, MCP exposure, sharing, and authorization (who may attach/query a collection) are owned by specs 009/010/012/013; this spec assumes a collection's authorization has already been evaluated before its retrieval partition (FR-016) is queried.
- Unified deletion across per-chat and Data Product scopes (PRD §4.22, REQ-FILE-3) is addressed here (FR-019, User Story 7) at the level of "chunks/vectors must not survive document deletion, with a tracked status"; Data Product deletion's underlying soft-delete mechanics remain owned by spec 013's Cleanup job rather than being redefined.
- The specific allow-listed file types and maximum size (PRD §4.22, REQ-FILE-1) are a configuration concern, not fixed by this spec; FR-020 requires that validation occur and rejection be actionable, not particular threshold values. Cleanup of citation records on deletion (also named in REQ-FILE-3) is already covered transitively by FR-019/FR-018 — a deleted document's chunks are excluded from retrieval, so no new citations referencing it can be produced; existing persisted citations are historical records of a past answer and are not retroactively deleted.
