# Product Requirements Document — AI Learning & Assistant Platform

> **Purpose of this document.** This PRD is a re-implementation specification derived from a full scan of the existing "ECPI AI Accelerator" codebase (an AI-powered learning platform integrated with Canvas LMS, built on the Allata Accelerator). It captures **what the system does** and **why**, expressed as product requirements rather than a description of the current code. It is intended to let a team build an equivalent system on a technology stack of their choosing.

- **Document status:** Draft v1.0
- **Source:** Reverse-engineered from the current application (Next.js 16 / React 19 / TypeScript / Azure).
- **Audience:** Product managers, architects, and engineers implementing a new system.

---

## 1. Overview

### 1.1 Product summary
The platform is a multi-tenant, role-aware **AI assistant and learning platform**. It lets an organization (originally a post-secondary education provider) give staff, faculty, and students access to conversational AI that can be grounded in the organization's own documents, packaged into reusable **personas** (pre-configured assistants), delivered inside a **learning management system (Canvas)** as lessons, and extended with tools and multi-agent workflows.

The system is organized around five pillars:

1. **Conversational AI chat** — streaming multi-model chat with tools, file understanding, images, and voice.
2. **Knowledge grounding (RAG)** — upload documents into searchable collections ("Data Products") and per-chat documents, retrieved via hybrid vector + keyword search with citations.
3. **Personas & prompts** — reusable, shareable assistant configurations and prompt templates.
4. **LMS/lesson delivery** — Canvas LTI integration that launches persona-driven "lessons" for students and submits work back to Canvas.
5. **Orchestration & agents** — visual multi-agent workflows and agent-to-agent (A2A) interoperability.

Cross-cutting concerns include authentication/roles, sharing & permissions, PII redaction, admin configuration, analytics, feedback, and observability.

### 1.2 Goals
- Provide a secure, enterprise-grade conversational AI experience with support for **multiple AI model providers** behind one interface.
- Ground AI responses in **organization-owned documents** with transparent citations.
- Allow non-developers to **create, share, and reuse assistants (personas)** and prompt templates.
- Deliver AI-assisted **lessons inside Canvas LMS** with automatic student-context awareness and assignment submission.
- Enforce **role-based access** to models, tools, and data.
- Protect sensitive data through **PII redaction** and least-privilege sharing.
- Give admins runtime control over models, limits, and configuration **without redeploying**.

### 1.3 Non-goals
- The system is not a general-purpose LMS; it augments an existing LMS (Canvas).
- It does not train or fine-tune foundation models; it consumes hosted model endpoints.
- It is cloud-hosted (originally Azure-only). Multi-cloud portability is desirable but not required (a previous AWS provider path has been retired as dead code).

---

## 2. User Roles & Personas

The system is role-aware. Roles are derived from the identity provider (enterprise SSO group membership) and, for students, from LMS launch context.

| Role | Description | Key capabilities |
|------|-------------|------------------|
| **Admin** | Platform administrators | Full access to all models/tools; system configuration; model access policy; message limits; MCP monitoring; can share with any group; can impersonate students; can manage lesson personas. |
| **Staff / Employee** | Internal staff | Standard chat, personas, prompts, data products; configurable sharing; broader model access. |
| **Faculty** (derived from "employee") | Instructors | Same as staff; create lesson personas & data products for courses; configurable group sharing. Note: "faculty" is a derived concept, not a distinct stored flag in the source system. |
| **Contractor** | External contributors | Similar to staff but may have narrower access. |
| **Student** | Learners (typically via Canvas LTI) | Read access to lesson personas; scoped chat; cannot create/delete lesson personas; most restricted model/tool set and sharing. |
| **Advanced-model user** | Flag layered on any role | Grants access to premium/"frontier" models and advanced-model-gated tools. |

Additional derived contexts:
- **Impersonated student (“Student View”)** — an admin temporarily downgraded to student privileges for QA/support; all elevated roles are suppressed at a single chokepoint, and the action is audited.
- **Canvas/LTI student session** — a session originating from an LMS launch, identified by course/user context rather than corporate email.

**REQ-ROLE-1** The system SHALL determine a user's roles from enterprise SSO group membership at sign-in.
**REQ-ROLE-2** Role elevation SHALL be suppressed whenever a session is in an impersonation ("student view") state, at a single enforcement point.
**REQ-ROLE-3** Students launched via the LMS SHALL be granted a scoped student session even without a corporate email address, using a stable unique identity derived from LMS course/user context.
**REQ-ROLE-4** All authorization decisions (model access, tool access, sharing, delete/write) SHALL be enforced server-side; UI gating is not a security boundary.

---

## 3. System Context & Architecture (reference)

The following describes the **capabilities the new system must provide**, expressed independent of specific vendor products where possible. The reference implementation's technology choices are noted for context.

- **Web application**: server-rendered app with authenticated and public route groups (reference: Next.js App Router). Public routes: health checks, login, LMS launch/error. All other routes require a valid session.
- **AI gateway**: a unified model registry that fronts multiple model providers with a single call interface and streaming responses (reference: Vercel AI SDK `streamText`).
- **Primary datastore**: a document database with **native vector search** for semantic retrieval (reference: Cosmos DB with DiskANN vector index + Reciprocal Rank Fusion hybrid search).
- **Object storage**: blob storage for uploaded files and generated images (reference: Azure Blob Storage).
- **Cache**: a distributed cache for configuration and rate limiting (reference: Redis, with in-memory fallback).
- **Document intelligence**: an OCR/text-extraction service for uploaded documents (reference: Azure Document Intelligence / Form Recognizer).
- **Embeddings**: a text-embedding service producing 1536-dimension vectors (reference: Azure OpenAI `text-embedding-3-small`).
- **Identity**: enterprise OAuth/OIDC SSO (reference: Azure AD via NextAuth) plus LMS LTI launch tokens.
- **Ingestion pipeline**: an event-driven pipeline for bulk document ingestion from an enterprise content source (reference: Logic Apps pulling from SharePoint).
- **Observability**: distributed tracing/metrics/logging (reference: OpenTelemetry → Azure Monitor).
- **Interoperability**: **MCP (Model Context Protocol)** servers and **A2A (Agent-to-Agent)** protocol support.

**REQ-ARCH-1** The system SHALL expose model providers through a single internal registry so that new models/providers can be added via configuration.
**REQ-ARCH-2** The system SHALL support server-authenticated model access using managed/workload identity (no long-lived API keys in code) where the provider supports it.
**REQ-ARCH-3** The system SHALL support runtime configuration overrides (models, limits, access policy) sourced from environment variables and/or a configuration store, cached with a fallback to hard-coded defaults.
**REQ-ARCH-4** The system SHALL degrade gracefully when optional dependencies (cache, legacy search, telemetry) are unavailable.

---

## 4. Functional Requirements

Requirements are grouped by capability area. Each area lists user-facing behavior and the rules the system must enforce.

### 4.1 Authentication, Sessions & Access Control

- Sign-in via enterprise SSO (OAuth/OIDC). A session carries: display name, email, avatar, roles (`isAdmin`, `isEmployee`, `isContractor`, `isStudent`), `advancedModelAccess`, an impersonation flag, and an access token.
- Student sessions may originate from an LMS launch (no email).
- Route protection: all application routes require a session except health, login, and LMS launch/error routes. Admin-only routes are additionally gated.
- Stable user identity: users are keyed by a hash of their normalized email; LMS students are keyed by a hash of `{lms_environment}:{lms_user_id}` so the same numeric user ID in different LMS environments maps to distinct identities.

**REQ-AUTH-1** The system SHALL authenticate users via enterprise SSO and establish a signed session.
**REQ-AUTH-2** The system SHALL protect all non-public routes and redirect unauthenticated users to sign-in.
**REQ-AUTH-3** The system SHALL restrict admin-only routes/actions to admins server-side.
**REQ-AUTH-4** The system SHALL hash user identifiers before using them as storage partition keys.
**REQ-AUTH-5** The system SHALL support a reversible "student view" impersonation for admins that downgrades privileges and writes an audit record for entry/exit.

### 4.2 Conversational Chat (core)

The chat experience is the primary surface.

- **Streaming responses** token-by-token from the selected model.
- **Model selection** per chat, constrained by the user's role-based allow-list.
- **Chat threads**: persistent, titled conversation histories per user; create, rename, delete, list, and continue.
- **Message persistence**: user and assistant messages stored with role, content, optional reasoning text, timestamps, and multimodal attachments.
- **Reasoning models**: reasoning/thinking content is handled separately (and can be stripped from output where appropriate).
- **Context window management**: when a thread grows large, older messages are **compressed/summarized** to stay within token limits, preserving a summary plus token accounting.
- **Message limits**: admins can configure per-user or per-role message limits; the system pre-flights a request against the limit before calling the model.
- **File attachments in chat**: users can attach documents/images to a message; documents are processed for retrieval ("Document Chat"), images are passed to multimodal models.
- **Markdown, math, code, diagrams**: responses render Markdown, KaTeX math, syntax-highlighted code, and Mermaid diagrams.
- **Citations**: when responses use retrieved documents, inline citations are attached and persisted.
- **Feedback**: users can rate conversations (1–5) and give per-message thumbs up/down, sampled by a configurable rate.

**REQ-CHAT-1** The system SHALL stream assistant responses incrementally to the client.
**REQ-CHAT-2** The system SHALL persist chat threads and messages per user and allow create/list/continue/rename/delete.
**REQ-CHAT-3** The system SHALL restrict selectable models to those permitted for the user's role/entitlements.
**REQ-CHAT-4** The system SHALL enforce a configurable message limit per user/role and reject or warn when exceeded, before invoking the model.
**REQ-CHAT-5** The system SHALL summarize/compress long threads to remain within the model context window while preserving a retrievable summary.
**REQ-CHAT-6** The system SHALL render Markdown, math, code highlighting, and diagrams in assistant output.
**REQ-CHAT-7** The system SHALL attach and persist citations for retrieval-grounded answers.
**REQ-CHAT-8** The system SHALL support attaching documents and images to a message and route them to retrieval or multimodal input respectively.
**REQ-CHAT-9** The system SHALL capture optional user feedback (conversation rating and per-message thumb) governed by a configurable sampling rate, and forward it to a feedback service.
**REQ-CHAT-10** The system SHALL enforce a maximum stored thread size and handle oversize threads safely.

### 4.3 AI Models & Provider Registry

The system fronts many model families through one registry. Observed providers/models include Azure-hosted foundation models (e.g., GPT-4o, GPT-4.1 / mini, GPT-5.x family, GPT-OSS), DeepSeek, Kimi, Llama, Mistral, plus Anthropic Claude and Google (Vertex) models.

- Each model has metadata: canonical ID (`provider:modelId`), display name, provider, capability flags (tool/function calling, vision, reasoning), and access tier.
- **Role-based model access**: an allow-list per role (admin/staff/faculty/student/default), overridable via configuration (env vars and/or config store), falling back to defaults.
- **Advanced/frontier models** are gated behind the `advancedModelAccess` entitlement.
- Provider-specific request/response adaptation (e.g., converting message roles, stripping provider-only fields, injecting managed-identity auth tokens) is handled centrally.

**REQ-MODEL-1** The system SHALL maintain a central catalog of models with capability and access metadata.
**REQ-MODEL-2** The system SHALL compute the effective set of models available to a user from role allow-lists plus entitlement gates, resolved server-side.
**REQ-MODEL-3** The system SHALL allow admins to override role→model access at runtime via configuration.
**REQ-MODEL-4** The system SHALL adapt requests/responses per provider so that a single chat interface works across providers.
**REQ-MODEL-5** The system SHALL authenticate to model endpoints using workload identity where supported and never embed long-lived secrets in client code.

### 4.4 Tools / Extensions

Chat can invoke server-side tools ("extensions") during a response. Observed tools: **Artifacts** (interactive code/UI artifacts), **Calculator**, **CanvasStudentContext**, **CSV Export**, **DataProduct** (query a knowledge collection), **DocumentChat** (query per-chat documents), **ImageGen** (image generation), **MapDisplay** (maps), **ResearchBot** / **WebSearch** (web research), **RouteTool** (routing/directions), **Weather**, and a **final-answer** control tool.

Each extension has gating metadata enforced in layers:
1. `requiresAdmin` — server-enforced admin-only (authoritative security boundary).
2. `isDemo` — UI visibility only.
3. `requiresAdvancedModel` — hidden when the active model lacks tool/function-calling capability.
4. `allowedModels` — a per-extension model allow-list (server- and client-enforced) used to restrict sensitive integrations to specific model tiers.

Extensions are grouped for the UI (Basic, Web, Microsoft 365, etc.). Tools have execution timeouts.

**REQ-TOOL-1** The system SHALL allow the model to call registered server-side tools during response generation and render their results.
**REQ-TOOL-2** The system SHALL enforce tool availability through admin gating, model-capability gating, and per-tool model allow-lists — all validated server-side before a tool is exposed to the model.
**REQ-TOOL-3** The system SHALL apply execution timeouts to tool calls and fail safely on timeout/error.
**REQ-TOOL-4** The system SHALL persist per-user/per-persona tool enablement (which tools are turned on) and re-validate it on each request.
**REQ-TOOL-5** The system SHALL provide, at minimum, tools for: knowledge-collection query, per-chat document query, web search/research, calculator, image generation, CSV export, maps, weather, and interactive artifacts.

### 4.5 Retrieval-Augmented Generation (RAG) & Knowledge Grounding

Two retrieval scopes exist:
- **Per-chat documents** ("Document Chat"): files a user attaches to a specific chat thread; scoped to that thread.
- **Data Products**: curated, reusable document collections (see §4.6) that can be attached to personas or queried as a tool.

Ingestion pipeline (per document):
1. Extract text via document-intelligence OCR (supports PDFs, office docs, images).
2. Chunk text (default ~1000 characters with ~200-character overlap, preserving word/sentence boundaries).
3. Generate embeddings in batches (e.g., 100 chunks/batch) producing 1536-dim vectors.
4. Bulk-write chunks + vectors + metadata to the vector store.

Retrieval at chat time:
- Embed the query, run **hybrid search** combining full-text score and vector distance via **Reciprocal Rank Fusion** (default weighting favors vector, e.g., ~0.3 text / ~0.7 vector), returning top-K (default ~100 for chat, configurable ~20 for data products).
- Per-chat documents are partitioned so retrieval is scoped to the current thread; data products are partitioned by collection ID.
- Format results into a context block with source attribution and produce **citations** for the answer.
- Ingestion status is tracked per document (pending → extracting → embedding → ingesting → completed/failed, with retry/dead-letter states).

**REQ-RAG-1** The system SHALL extract text from uploaded documents (PDF, office formats, images) via OCR/document intelligence.
**REQ-RAG-2** The system SHALL chunk extracted text with configurable size/overlap preserving natural boundaries.
**REQ-RAG-3** The system SHALL generate vector embeddings for chunks and store them with source metadata in a vector-searchable store.
**REQ-RAG-4** The system SHALL retrieve context via hybrid (keyword + vector) search with rank fusion and configurable top-K.
**REQ-RAG-5** The system SHALL scope per-chat document retrieval to the originating thread and data-product retrieval to the selected collection(s).
**REQ-RAG-6** The system SHALL produce and persist citations linking answer content to source documents.
**REQ-RAG-7** The system SHALL track per-document ingestion status with retry and failure handling, and expose it to owners/admins.
**REQ-RAG-8** The system SHALL support deleting documents and their derived vectors/chunks, with deletion status tracking.

### 4.6 Data Products (knowledge collections)

A **Data Product** is a self-contained, ownable, shareable collection of documents used for grounding.

- Create/rename/delete a data product; upload files into it; enable/disable individual documents.
- Documents are ingested through the RAG pipeline (§4.5) and versioned.
- **Health monitoring**: track ingestion health and surface failures.
- **Rate limiting**: protect ingestion/query endpoints.
- **Audit**: record significant actions (create, upload, delete).
- **MCP exposure**: a data product can be exposed via an MCP service so external agents/tools can query it.
- **Sharing**: data products can be shared with individuals/groups (see §4.10) and attached to personas.
- Bulk ingestion: documents may also arrive via the enterprise content ingestion pipeline (e.g., SharePoint via Logic Apps), not only manual upload.

**REQ-DP-1** The system SHALL let authorized users create knowledge collections and upload documents into them.
**REQ-DP-2** The system SHALL allow enabling/disabling individual documents within a collection without deleting them.
**REQ-DP-3** The system SHALL version documents and track ingestion health per collection.
**REQ-DP-4** The system SHALL rate-limit collection ingestion/query operations.
**REQ-DP-5** The system SHALL audit create/upload/delete actions on collections and documents.
**REQ-DP-6** The system SHALL support sharing collections with individuals and groups and attaching them to personas.
**REQ-DP-7** The system SHALL optionally expose a collection through an MCP endpoint for external tool/agent access, with appropriate authorization.
**REQ-DP-8** The system SHALL support automated bulk ingestion from an enterprise content source in addition to manual upload.

### 4.7 Personas (reusable assistants)

A **Persona** is a saved assistant configuration.

Persona attributes: name, description, system/persona message (instructions), default model, optional starting message, enabled extensions, attached data products, sharing list, collaborators, owner/creator, timestamps, optional comment, `a2aEnabled` flag, optional API key (for programmatic/A2A access), and `isLessonPersona` flag.

- Create, edit, delete, duplicate/transfer ownership, and favorite personas.
- Start a chat directly from a persona (applies its model, instructions, tools, and data products).
- **Sharing & collaboration**: personas can be shared (read) and have collaborators (edit).
- **Lesson personas** (`isLessonPersona: true`): admin/faculty-managed teaching assistants used in LMS lessons. Students get **read** access to lesson personas but cannot edit or delete them; a hard guard blocks non-admins from deleting lesson personas.
- **Persona Studio**: a richer authoring experience (feature-flagged) for building personas.
- **Persona generation**: AI-assisted generation of a persona from a short description, using an admin-configured generation model.
- **A2A**: a persona can be published as an agent (agent card) callable via the A2A protocol using its API key.

**REQ-PERSONA-1** The system SHALL let authorized users create/edit/delete/duplicate personas capturing model, instructions, starting message, enabled tools, and attached knowledge collections.
**REQ-PERSONA-2** The system SHALL let users start a chat from a persona, applying all persona configuration.
**REQ-PERSONA-3** The system SHALL support favoriting personas per user.
**REQ-PERSONA-4** The system SHALL support sharing personas (read) and adding collaborators (edit) with server-enforced authorization.
**REQ-PERSONA-5** The system SHALL designate "lesson personas" that students may read/use but not edit or delete; deletion of lesson personas SHALL be restricted to admins.
**REQ-PERSONA-6** The system SHALL provide AI-assisted persona generation from a natural-language description using an admin-configured model.
**REQ-PERSONA-7** The system SHALL optionally publish a persona as an A2A-callable agent secured by an API key.
**REQ-PERSONA-8** The system SHALL exclude sensitive persona fields (e.g., API keys) from client-facing responses.

### 4.8 Prompts (templates)

A **Prompt** is a reusable prompt template (title + body/description).

- Create/edit/delete prompts; favorite them; share/transfer ownership.
- Start a chat pre-seeded with a prompt.
- Prompts appear in a library and can be set as a user's landing action.

**REQ-PROMPT-1** The system SHALL let users create/edit/delete reusable prompt templates.
**REQ-PROMPT-2** The system SHALL support favoriting, sharing, and ownership transfer of prompts.
**REQ-PROMPT-3** The system SHALL let users launch a chat pre-populated from a prompt.

### 4.9 Multi-Chat (model comparison)

- Users can send one message to **multiple models simultaneously** and compare responses side-by-side in a single view.

**REQ-MULTICHAT-1** The system SHALL let users query multiple models in parallel and display their responses side-by-side for comparison.

### 4.10 Sharing & Permissions

A cross-cutting sharing system governs personas, prompts, and data products.

- Share targets: **individuals** (by identity) and **groups** (admins, staff, faculty, students).
- Per-role sharing policy (configurable): who may share with groups vs. individuals, and which group targets are allowed.
  - Admin: share with all groups and individuals.
  - Faculty: individuals always; group sharing configurable (faculty/students).
  - Student: individuals always; group sharing configurable (students only).
- **Global overrides**: emergency disable of all group sharing; admin-only sharing mode; globally allowed group targets (e.g., announcements).

**REQ-SHARE-1** The system SHALL support sharing owned resources with individuals and with predefined organizational groups.
**REQ-SHARE-2** The system SHALL enforce a configurable, role-based sharing policy that constrains valid share targets per role, server-side.
**REQ-SHARE-3** The system SHALL support global sharing overrides (disable-all-group-sharing, admin-only mode, globally allowed groups) for emergency/administrative control.
**REQ-SHARE-4** Shared resources SHALL grant read access by default; edit access requires explicit collaborator designation.

### 4.11 Canvas LMS / LTI Integration & Lessons

The platform integrates with Canvas LMS to deliver AI lessons and interact with course data.

- **LTI launch**: Canvas launches a lesson via a signed launch token. A proxy intercepts `/lesson/{personaId}?token={jwt}`, exchanges the token for a platform session, and redirects to the clean lesson URL. Launch tokens are validated (signature, claims, JTI replay protection via a cache). Errors route to a dedicated LTI error page.
- **Student session from launch**: produces a scoped `isStudent` session keyed by `{canvas_env}:{canvas_user_id}`.
- **Lesson mode**: a distinct UI mode that pins a lesson persona, hides unrelated navigation, and provides an explicit "exit lesson" affordance.
- **Canvas student context tool**: an extension that fetches the student's Canvas context (course, assignment) to ground the assistant.
- **Canvas OAuth tokens**: users can connect their Canvas account via OAuth; tokens are encrypted at rest, refreshable, and health-checked. Used to read course/assignment data and **submit assignments** back to Canvas.
- **PII redaction for Canvas**: content exchanged in Canvas flows is run through PII redaction.
- **Multiple Canvas environments** are supported (distinct environments must not collide in identity).

**REQ-LTI-1** The system SHALL accept and validate signed LMS launch tokens (signature, required claims, replay protection) and exchange them for a scoped platform session.
**REQ-LTI-2** The system SHALL render a dedicated lesson mode that pins the lesson persona and restricts navigation, with an explicit exit path.
**REQ-LTI-3** The system SHALL support per-user OAuth connection to the LMS, storing tokens encrypted at rest with refresh and health checks.
**REQ-LTI-4** The system SHALL provide a tool to retrieve the student's LMS course/assignment context to ground responses.
**REQ-LTI-5** The system SHALL support submitting student work/assignments back to the LMS.
**REQ-LTI-6** The system SHALL support multiple LMS environments without identity collisions.
**REQ-LTI-7** The system SHALL route LMS launch/validation failures to a user-friendly error page with diagnostic codes.

### 4.12 Orchestration (multi-agent workflows)

A visual builder for multi-step, multi-agent workflows.

- A workflow is a graph of **nodes** (personas/agents/tools) connected by **connections**, with **triggers** (API trigger, file-upload trigger, multi-modal trigger) and **terminal outputs**.
- Triggers carry configuration: API keys, allowed origins, rate limits, accepted file types/sizes/counts, processing modes (individual/batch/combined), context-passing modes, virus-scan requirements, retention.
- **Execution**: workflows execute node-by-node, streaming intermediate results; execution state, per-node results, message history, and snapshots are persisted.
- **Validation**: node and connection validation before/at execution.
- Feature-flagged ("Orchestration Studio").

**REQ-ORCH-1** The system SHALL let authorized users build workflows as graphs of persona/agent/tool nodes with typed connections.
**REQ-ORCH-2** The system SHALL support workflow triggers (API, file-upload, multi-modal) with per-trigger configuration (auth, rate limits, file constraints, retention).
**REQ-ORCH-3** The system SHALL validate node and connection configuration prior to execution.
**REQ-ORCH-4** The system SHALL execute workflows, stream intermediate node results, and persist execution state, per-node results, message history, and snapshots.
**REQ-ORCH-5** The system SHALL gate the orchestration builder behind a feature flag.

### 4.13 Agents & Interoperability (A2A + MCP)

- **A2A (Agent-to-Agent)**: personas can be exposed as agents with **agent cards**; an A2A chat service lets external agents converse with a persona-backed executor, authenticated by API key.
- **MCP (Model Context Protocol)**: the platform can act as an MCP host (consuming external MCP servers/tools) and can expose resources (e.g., data products) via MCP. MCP activity is logged and monitored (admin MCP monitoring dashboard).

**REQ-AGENT-1** The system SHALL be able to publish personas as A2A agents (with agent cards) and serve A2A conversations authenticated by API key.
**REQ-AGENT-2** The system SHALL support consuming external MCP servers/tools to extend assistant capabilities.
**REQ-AGENT-3** The system SHALL expose selected internal resources (e.g., knowledge collections) via MCP with authorization.
**REQ-AGENT-4** The system SHALL log MCP interactions and provide an admin monitoring view.

### 4.14 Image Generation & Vision

- Users can generate images from prompts via an image-generation tool; generated images are validated and stored in blob storage and referenced in chat.
- Multimodal models can accept image inputs (attachments) for vision tasks.

**REQ-IMG-1** The system SHALL support AI image generation from prompts, storing outputs in object storage and rendering them in chat.
**REQ-IMG-2** The system SHALL validate generated/received images (type/size) before storage/use.
**REQ-IMG-3** The system SHALL pass user-supplied images to vision-capable models.

### 4.15 Voice Chat

- Real-time voice conversation using a low-latency realtime model (WebRTC-style offer/answer session negotiation and a session/token endpoint).

**REQ-VOICE-1** The system SHALL support a real-time voice conversation mode using a realtime speech model, with secure session/token negotiation.

### 4.16 Artifacts

- The assistant can produce **artifacts** (e.g., runnable/rendered code or UI components) displayed in a dedicated panel with its own state store, separate from the chat transcript.

**REQ-ARTIFACT-1** The system SHALL support generating and displaying interactive artifacts (code/UI) in a dedicated panel with persisted state, distinct from chat messages.

### 4.17 PII Redaction

- A redaction service detects and redacts personally identifiable information in content (used in Canvas flows and configurable elsewhere), with a configurable policy.

**REQ-PII-1** The system SHALL detect and redact PII in designated content flows according to a configurable policy.
**REQ-PII-2** PII redaction SHALL be applied to LMS/Canvas content exchanges.

### 4.18 Admin Configuration

Admins configure the platform at runtime:
- **Model access policy** (role→model allow-lists).
- **Persona generation model** selection.
- **Message limits** (per user/role).
- **System configuration** (cached with fallback to defaults).
- **MCP monitoring** dashboard.

**REQ-ADMIN-1** The system SHALL provide an admin surface to configure role-based model access, the persona-generation model, and message limits without redeployment.
**REQ-ADMIN-2** The system SHALL cache system configuration with a resilient fallback to defaults.
**REQ-ADMIN-3** The system SHALL provide an admin MCP monitoring view.
**REQ-ADMIN-4** All admin surfaces and actions SHALL be restricted to admins server-side.

### 4.19 User Preferences

- Theme (light/dark) selection.
- **Landing page preference**: new chat, a favorite persona (opens a direct chat), or a favorite prompt (opens a pre-seeded chat). Invalid/stale preferences fall back to a new chat and self-heal.

**REQ-PREF-1** The system SHALL persist per-user preferences including theme and default landing action.
**REQ-PREF-2** The system SHALL resolve the landing action (new chat / favorite persona / favorite prompt) and gracefully fall back when the referenced resource no longer exists.

### 4.20 Analytics / Executive Dashboard

- An executive/statistics view surfaces usage analytics (aggregated statistics), with caching.

**REQ-ANALYTICS-1** The system SHALL provide an analytics dashboard summarizing platform usage, backed by cached aggregate statistics.

### 4.21 Changelog & Notifications

- A **changelog** authored in structured content (Markdoc) is rendered in-app.
- A **version alert** notifies users when a new app version is available.
- Real-time notifications (e.g., bulk-delete completion, ingestion updates) are delivered via WebSocket.

**REQ-NOTIF-1** The system SHALL present an in-app changelog rendered from structured content.
**REQ-NOTIF-2** The system SHALL notify users when a newer application version is available.
**REQ-NOTIF-3** The system SHALL deliver real-time notifications (e.g., long-running operation completion) to connected clients.

### 4.22 File Upload & Processing

- Users upload files (chat attachments, data-product documents). The system validates type/size, stores originals in object storage, extracts text, and handles password-protected/corrupt files with clear errors.
- Deletion is unified across chat documents and data-product documents with status tracking.

**REQ-FILE-1** The system SHALL validate uploaded files (type, size) and store originals in object storage.
**REQ-FILE-2** The system SHALL handle unreadable inputs (password-protected/corrupt) with actionable error messages.
**REQ-FILE-3** The system SHALL provide unified document deletion (chat and collection scopes) with status tracking and cleanup of derived data (chunks/vectors/citations).

---

## 5. Data Model (logical)

The reference system uses a single document store with typed records discriminated by a `type` attribute and partitioned by owner/scope. Core entities:

| Entity | Key attributes | Notes |
|--------|----------------|-------|
| **User** | hashed id, email, roles, entitlements | Identity from SSO or LMS launch. |
| **Chat Thread** | id, userId, title, model, timestamps, persona ref | v1 and v2 message shapes exist. |
| **Chat Message** | id, threadId, userId, role, content, reasoning, attachments, timestamps | v2 stores UI message arrays. |
| **Compression Message** | threadId, summary, compressed/original token counts | Context-window management. |
| **Citation** | id, threadId, source doc refs, content | Persisted alongside answers. |
| **Chat Document** | per-thread uploaded document + chunks/vectors | Partitioned to a `DOCUMENT_CHAT` scope filtered by thread id. |
| **Persona** | id, userId, name, description, personaMessage, model, extensions[], dataProducts[], sharedWith[], collaborators[], a2aEnabled, apiKey, isLessonPersona | API key excluded from client DTO. |
| **Persona Favorite** | userId, personaIds[] | Per-user favorites. |
| **Prompt** | id, userId, name/title, description/body, sharing | Templates. |
| **Data Product** | id, ownerId, name, sharing, search config | Collection metadata. |
| **Data Product Document** | id, dataProductId, fileName, version, isEnabled, ingestionStatus, deletionStatus | Ingestion/deletion lifecycle fields. |
| **Document Chunk (vector)** | content.text, vectors.text_embedding (1536), vectors.image_embedding, source metadata, processing metadata | Vector-indexed. |
| **Orchestration** | nodes[], connections[], triggers, config | Workflow definition. |
| **Orchestration Execution / Node Result / Message History / Snapshot** | execution state, per-node results | Runtime state. |
| **Feedback** | thread/message/persona refs, score type & value, optional text | Forwarded to feedback service, not stored locally in reference impl. |
| **System / Model Config** | role→model maps, generation model, message limits | Cached config. |
| **Canvas Token** | userId, encrypted access/refresh tokens, expiry, health | Encrypted at rest. |
| **Impersonation Audit** | actorId, action (enter/exit), timestamp | Student-view audit. |

**REQ-DATA-1** The system SHALL partition user-owned data by a hashed user (or LMS) identity.
**REQ-DATA-2** The system SHALL store document chunks with their vectors and enough source metadata to render citations.
**REQ-DATA-3** The system SHALL encrypt sensitive stored secrets (e.g., LMS OAuth tokens) at rest.
**REQ-DATA-4** The system SHALL soft-delete documents and track deletion status before purging derived vectors/chunks/citations.

---

## 6. External Integrations

| Integration | Purpose |
|-------------|---------|
| Enterprise SSO (OAuth/OIDC) | Authentication, roles from group membership. |
| Canvas LMS (LTI + OAuth API) | Lesson launch, student context, assignment submission. |
| Model providers (Azure-hosted foundation models, Anthropic, Google Vertex, OpenAI-compatible) | LLM inference. |
| Embedding service | Vector embeddings for RAG. |
| Document intelligence / OCR | Text extraction from uploads. |
| Object storage | Files & generated images. |
| Vector document database | Persistence + hybrid search. |
| Distributed cache | Config caching, rate limiting. |
| Enterprise content source (e.g., SharePoint) via ingestion pipeline | Bulk document ingestion. |
| Web search / research provider | Web-grounded answers. |
| Maps / geocoding provider | Map display & routing tools. |
| Weather provider | Weather tool. |
| Feedback service (service-to-service, token-auth) | Collect user feedback. |
| MCP servers | External tools/data. |
| A2A protocol | Agent interoperability. |
| Telemetry backend (OpenTelemetry) | Tracing, metrics, logs. |

**REQ-INT-1** Each external integration SHALL be abstracted behind a provider/factory boundary so alternate implementations can be substituted.
**REQ-INT-2** Service-to-service calls SHALL use short-lived tokens (workload identity / service tokens) rather than static secrets where supported.

---

## 7. Non-Functional Requirements

### 7.1 Security & Privacy
**REQ-NFR-SEC-1** All authorization SHALL be enforced server-side; client gating is advisory only.
**REQ-NFR-SEC-2** Sensitive fields (API keys, tokens) SHALL never be sent to clients or logged.
**REQ-NFR-SEC-3** The system SHALL redact PII in designated flows (LMS/Canvas and configurable content).
**REQ-NFR-SEC-4** LMS launch tokens SHALL be validated for signature, required claims, and replay (JTI) protection.
**REQ-NFR-SEC-5** Uploaded content and outbound URLs SHALL be validated (type/size, SSRF-safe URL validation) to mitigate OWASP Top 10 risks.
**REQ-NFR-SEC-6** The system SHALL sanitize rendered model/user content to prevent injection/XSS.
**REQ-NFR-SEC-7** Impersonation SHALL be least-privilege and fully audited.

### 7.2 Performance & Scalability
**REQ-NFR-PERF-1** Chat responses SHALL stream with low time-to-first-token.
**REQ-NFR-PERF-2** Retrieval SHALL use an approximate-nearest-neighbor vector index (e.g., DiskANN) for scalable similarity search.
**REQ-NFR-PERF-3** Configuration and hot-path metadata SHALL be cached with a resilient fallback.
**REQ-NFR-PERF-4** Ingestion SHALL batch embedding generation and bulk-write to the store.
**REQ-NFR-PERF-5** The system SHALL support multi-tenant scale via partition-key strategies and pagination (continuation tokens).

### 7.3 Reliability & Resilience
**REQ-NFR-REL-1** Optional dependencies (cache, legacy search, telemetry) SHALL fail open without breaking core chat.
**REQ-NFR-REL-2** Ingestion SHALL support retries, dead-letter handling, and status visibility.
**REQ-NFR-REL-3** The system SHALL expose health-check endpoints for liveness/readiness.
**REQ-NFR-REL-4** Long threads SHALL be compressed to avoid context-window failures.

### 7.4 Observability
**REQ-NFR-OBS-1** The system SHALL emit distributed traces, metrics, and structured logs (OpenTelemetry-compatible).
**REQ-NFR-OBS-2** MCP and tool executions SHALL be logged for monitoring/debugging.

### 7.5 Accessibility & UX
**REQ-NFR-UX-1** The UI SHALL support light/dark themes and responsive layouts.
**REQ-NFR-UX-2** The UI SHALL render Markdown, math, code, and diagrams accessibly.
**REQ-NFR-UX-3** Feature availability SHALL be controllable via feature flags for staged rollout.

### 7.6 Configurability
**REQ-NFR-CFG-1** Organization-specific settings (org name, group IDs, sharing policy, role→model access, limits, feature flags) SHALL be externalized to configuration for reuse across deployments/clients.

### 7.7 Testability & Quality
**REQ-NFR-TEST-1** I/O boundaries (DB, storage, model providers, auth) SHALL be abstracted so they can be substituted in tests.
**REQ-NFR-TEST-2** Core services and components SHALL have automated unit/integration tests.

---

## 8. Assumptions & Constraints

- The platform augments an existing LMS (Canvas); it does not replace it.
- The organization uses enterprise SSO with group-based role assignment.
- Model providers are consumed as hosted endpoints; no model training/fine-tuning is in scope.
- The reference deployment targets a single cloud (Azure); provider abstractions exist but multi-cloud is not a hard requirement.
- Feature flags gate not-yet-GA features (e.g., Persona Studio, Orchestration Studio).

---

## 9. Out of Scope (for the new system unless re-prioritized)
- Model fine-tuning/training.
- Full LMS grading workflows beyond assignment submission and context retrieval.
- Native mobile apps (web-first).
- Billing/metering of AI usage (only message limits are enforced).

---

## 10. Glossary
- **Persona** — a saved, shareable assistant configuration (instructions + model + tools + knowledge).
- **Data Product** — a curated, shareable document collection used for grounding.
- **Extension/Tool** — a server-side capability the model can invoke during a response.
- **RAG** — Retrieval-Augmented Generation; grounding answers in retrieved documents.
- **Lesson Persona** — an admin/faculty-managed persona delivered to students via the LMS.
- **Orchestration** — a visual multi-agent workflow.
- **A2A** — Agent-to-Agent interoperability protocol.
- **MCP** — Model Context Protocol for connecting external tools/data.
- **Student View / Impersonation** — an admin operating with downgraded student privileges for support/QA.
- **Hybrid search / RRF** — combining keyword and vector search via Reciprocal Rank Fusion.

---

## Appendix A — Capability → Requirement Traceability (summary)

| Capability area | Requirement IDs |
|-----------------|-----------------|
| Roles & access | REQ-ROLE-1..4, REQ-AUTH-1..5 |
| Chat core | REQ-CHAT-1..10 |
| Models & registry | REQ-MODEL-1..5, REQ-ARCH-1..4 |
| Tools/extensions | REQ-TOOL-1..5 |
| RAG | REQ-RAG-1..8 |
| Data products | REQ-DP-1..8 |
| Personas | REQ-PERSONA-1..8 |
| Prompts | REQ-PROMPT-1..3 |
| Multi-chat | REQ-MULTICHAT-1 |
| Sharing | REQ-SHARE-1..4 |
| Canvas/LTI | REQ-LTI-1..7 |
| Orchestration | REQ-ORCH-1..5 |
| Agents (A2A/MCP) | REQ-AGENT-1..4 |
| Images/vision | REQ-IMG-1..3 |
| Voice | REQ-VOICE-1 |
| Artifacts | REQ-ARTIFACT-1 |
| PII redaction | REQ-PII-1..2 |
| Admin config | REQ-ADMIN-1..4 |
| Preferences | REQ-PREF-1..2 |
| Analytics | REQ-ANALYTICS-1 |
| Changelog/notifications | REQ-NOTIF-1..3 |
| File handling | REQ-FILE-1..3 |
| Data model | REQ-DATA-1..4 |
| Integrations | REQ-INT-1..2 |
| Non-functional | REQ-NFR-* |
