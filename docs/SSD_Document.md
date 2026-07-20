# Master Repository SDD Discovery Registry
> **System Architecture Era:** Modern (Next.js 16 App Router, React 19, TypeScript, Tailwind 4) — Azure-only cloud target; contains extensive dead/vestigial code from a prior AWS/Bedrock-pluggable-provider architecture that was nominally removed but never fully purged.
> **Total Domains Scanned:** (1) Auth & Identity, (2) Chat Core, (3) Multi-Chat / Chat-Home / Lesson Mode, (4) Canvas LTI Integration & Student View, (5) Persona Management & Persona Studio, (6) Orchestration & Agents (A2A), (7) Data Products, (8) Admin / Settings / Executive Reporting, (9) Prompt Library, (10) Common Cross-Cutting Services (DB, Storage, Search, Sharing, PII, AI Registry), (11) Support Features (Feedback, Changelog, Health, Version-Alert, Main-Menu)

---

## 1. High-Level System Architecture & Domain Boundaries

The system is an AI learning-platform accelerator: authenticated users (admin/staff/faculty/student, plus Canvas-LTI-launched students) chat with configurable "Personas" (system-prompted AI agents) that can invoke tools (web search, calculator, weather, maps, CSV/artifact generation, RAG document search, "Data Products," and Canvas LMS context), build multi-step "Orchestrations" that delegate across personas (also exposed externally as A2A agents), and submit lesson work back to Canvas. Admins configure models, message limits, and view executive usage analytics.

- **Domain A: Auth & Identity** — Azure AD OAuth session management, role derivation from AD groups, Canvas-LTI JWT-based session bootstrap, and admin "Student View" impersonation. Paths: `features/auth-page/*`, `app/api/auth/*`, `app/(authenticated)/api/auth/*`, `types/next-auth.d.ts`.
- **Domain B: Chat Core** — the streaming chat pipeline: message limits, prompt assembly, PII redaction, tool orchestration, model access control, thread/message persistence, document ingestion/RAG, PDF export, artifacts. Paths: `features/chat-page/**`, `app/(authenticated)/api/chat/**`, `app/(authenticated)/chat/**`.
- **Domain C: Multi-Chat / Chat-Home / Lesson Mode** — parallel-persona chat panes, favorites-driven landing page, and the Canvas-lesson-scoped chat experience (gating, sidebar scoping, Canvas submission). Paths: `features/multi-chat-page/*`, `features/chat-home-page/*`, `features/lesson-chat/*`, `features/lesson-mode/*`, `app/(authenticated)/multi-chat/**`, `app/(authenticated)/lesson/**`.
- **Domain D: Canvas LTI Integration & Student View** — LTI launch validation (via an external Canvas Integration Service), Canvas OAuth for chat-tool data access, PII-redacted Canvas MCP tools, assignment submission, and admin-as-student impersonation. Paths: `features/canvas/*`, `features/canvas-integration/*`, `features/student-view/*`, `app/lti/**`, `app/(authenticated)/api/canvas/**`, `app/api/auth/canvas-launch/**`.
- **Domain E: Persona Management & Persona Studio** — persona CRUD/authorization, sharing, AI-assisted persona building, live draft preview, and the A2A-facing agent surface. Paths: `features/persona-page/*`, `features/persona-studio/*`, `app/(authenticated)/persona*/**`, `app/api/personas/**`.
- **Domain F: Orchestration & Agents (A2A)** — visual workflow builder/executor with persona-to-persona delegation, and the external Agent-to-Agent JSON-RPC surface exposing personas as agents. Paths: `features/orchestration-page/*`, `features/agents/*`, `app/(authenticated)/orchestration/**`, `app/api/agents/**`.
- **Domain G: Data Products** — curated, shareable "knowledge sources" (URLs/files) with hybrid-search retrieval, exposed both to in-app chat tools and an external MCP endpoint, backed by an asynchronous Azure Logic Apps ingestion pipeline. Paths: `features/dataproducts-page/*`, `app/(authenticated)/dataproduct/**`, `app/api/dataproduct/**`.
- **Domain H: Admin / Settings / Executive Reporting** — system-wide model registry & role-based access configuration, message-limit policy, persona-generation model policy, and executive usage-statistics dashboard. Paths: `features/admin/*`, `features/admin-settings/*`, `features/executive-page/*`, `features/user-preferences/*`, `app/(authenticated)/admin/**`, `app/(authenticated)/executive/**`.
- **Domain I: Prompt Library** — reusable saved prompts, sharing, ownership transfer, and AI-assisted prompt generation. Paths: `features/prompt-page/*`, `app/(authenticated)/prompt/**`, `app/api/prompts/**`.
- **Domain J: Common Cross-Cutting Services** — the shared DB/storage/AI-model abstraction layer, document processing, file upload, sharing-permission engine, PII redaction, and telemetry. Paths: `features/common/**`, `features/pii-redaction/*`, `lib/*`.
- **Domain K: Support Features** — feedback collection (proxied to an external ECPI service), changelog, health/readiness probes, version-mismatch alerting, and role-aware main menu. Paths: `features/feedback/*`, `features/changelog-page/*`, `features/version-alert/*`, `features/main-menu/*`, `app/health/**`, `app/api/health/**`.

---

## 2. Global Data Dictionary & Domain Objects

* **`UserModel`** (session-derived, not a DB row): `name`, `email`, `image`, `token` (OAuth access token); role flags `isAdmin`, `isEmployee`, `isContractor`, `isStudent` (booleans, derived from AAD security-group membership); `advancedModelAccess` (premium-model gate); `impersonateAsStudent?` (true only while an admin is in Student View — forces all other role flags false). No `isFaculty` field exists; "faculty" is a UI label derived from `isEmployee`.

* **`PersonaModel`**: `id`, `userId` (hashed owner), `model` (must resolve against the live model registry), `name`/`description`/`personaMessage` (system prompt) — all required non-empty strings; `startingMessage?`, `extensions?: RegisteredExtensions[]`, `dataProducts?: string[]` (required non-empty only when `DataProduct` extension selected — enforced only client-side, not in the Zod schema), `sharedWith?`/`collaborators?` (emails/`@group` tokens), `a2aEnabled?`, `apiKey?` (A2A credential — must be stripped before reaching client code via `PersonaPublicDTO`), `comment?` (≤600 chars), `isLessonPersona?` (admin-managed Canvas teaching resource, drives the student-read-only carve-out).

* **`ChatThreadModel`**: `id`, `userId`, `name`, `personaId?`, `personaMessage`, `model`, `version` (`"v1"|"v2"|"v3"` — only `v3` threads are writable; older versions are read-only), `extensions?`, `dataProducts?: string[]`, `startingMessage?`; sharing fields (`isShared`, `shareId`, `sharedBy`, `sharedAt` — sharing has no dedicated entity, it's flags on the thread, and shares never expire); `clonedFromShareId?`; multi-chat fields (`multiChatSessionId?`, `multiChatPosition?`); `isLessonThread?` (drives a 90-day Cosmos TTL). `MAX_CHAT_THREAD_SIZE = 2,000,000`.

* **`ChatMessageModelV2`**: one Cosmos doc holding `messages: AcceleratorUIMessage[]` (AI SDK v5 `UIMessage` with `parts[]`); `MessageMetadata` carries `modelProvider?`, `totalTokens?`, `compressionEvent?` (original/compressed token counts, messages compressed).

* **`ChatDocumentModel`** / **`DataProductDocumentModel`**: uploaded-file metadata — `id`, `name`, `threadId`/`dataProductId`, `fileName`, `ingestionStatus` (multi-state enum; most states are AWS-pipeline vestiges never reached by the current Cosmos-only pipeline), plus dead AWS-era fields (`s3ObjectKey?`, `bedrockDocumentId?`, `deletionMessageId?` (SQS), `lambdaProcessing?`).

* **`OrchestrationModel`**: `id`, `userId`, `name`/`description`, `version` (string counter, auto-incremented on graph change), `nodes: OrchestrationNode[]` (`type: 'persona'|'trigger'|'terminal'`), `connections: OrchestrationConnection[]` (`condition?` field exists in schema but is unused by the executor), `entryNode?`, `terminalNodes: string[]`, `globalPrompt?`.

* **`AgentCard`** (`@a2a-js/sdk`): `name`, `description`, `url: /api/agents/{personaId}`, `capabilities`, `skills[]` (one per persona extension) — describes a persona as an externally-callable A2A agent.

* **`DataProductModel`**: `id`, `userId`, `name`/`description` (required non-empty), `searchDepth?`, `isDeleted` (soft delete only), `collaborators?`/`sharedWith?`, `apiKey?` (MCP credential), `mcpEnabled?`, `searchProvider: "cosmosdb"`, `migrationStatus: legacy|migrating|migrated|failed`. No `source`/`schema`/`columns` field — a "data product" is a container of ingested URLs/files, not a structured dataset descriptor.

* **`PromptModel`**: `id`, `userId`, `name` (title) and `description` (this field *is* the actual reusable prompt text — there is no separate `content`/`template` field), `sharedWith?`, `collaborators?`, `isPublished?` (legacy bool, superseded by `sharedWith` array). No template-variable/placeholder engine exists.

* **`SystemModelConfig`** (admin singleton): per-role model allow-lists (`roleModelAccess.{admin,staff,faculty,student,default}`, each `string[]` or `"*"`), plus embedding/image/artifact/fallback model selections.

* **`ModelConfigDocument`**: per-model registry entry — `id`(=model id), `provider`, feature flags (`enableExtensions`, `enableSystemPrompt`, `enableMultiModal`, `enableStreaming`, `requiresAdvancedModelAccess`), `contextWindowSize`, pricing, `isEnabled`, `isDeleted` (soft delete). `ModelAliasDocument` maps retired model IDs forward.

* **`IDatabaseProvider`** (Cosmos abstraction): CRUD (`upsertItem`, soft-delete `deleteItem`), query (`findAllByUserId`, `findAllByType`, `findSharedWithUser`, `executeAggregationQuery`), batch/transaction (capped at 100 ops/partition — not a true atomic transaction), health check, atomic `incrementField`.

* **`ServerActionResponse<T>`** (app-wide result envelope): `{status:"OK", response:T} | {status:"ERROR"|"NOT_FOUND"|"UNAUTHORIZED", errors:[{message}]}` — used almost universally instead of thrown exceptions for expected failure modes.

* **`CanvasLaunchJWT`** (external contract, v1.0): `iss` (pinned `"canvas-integration-service"`), `aud` (pinned `"accelerator-app"`), `exp`/`nbf`/`iat`/`jti`, `canvas_env` (`"ECPI"|"NTT"|"SkillOps"`), `canvas_user_id`, `canvas_course_id`, `canvas_assignment_id?`.

* **`ImpersonateCookiePayload`**: `{userIdHash, mode:"student", exp}` — HMAC-SHA256-signed, not a JWT; drives admin Student-View impersonation.

---

## 3. Comprehensive Domain Deep-Dives

### 3.1 Domain: Auth & Identity
* **Source Sub-directories:** `features/auth-page/`, `app/lti/`, `app/login/`, `app/api/auth/`, `app/(authenticated)/api/auth/`
* **Core Business Logic Actions:**
  - **Session resolution:** WHEN a request needs the current user, THE SYSTEM SHALL call `getServerSession()` and map to `UserModel`; WHEN `impersonateAsStudent === true`, THE SYSTEM SHALL force-downgrade all elevated role flags to `false` and `isStudent` to `true` — this downgrade is independently re-implemented in three places (`jwt()` callback, `session()` callback, `userSession()`).
  - **Role derivation:** WHEN Azure AD `groups` claims contain a configured group GUID, THE SYSTEM SHALL map it to `isAdmin`/`isEmployee`/`isStudent` (`isContractor` is permanently hardcoded `false` — no AD group configured for it).
  - **Canvas LTI launch:** WHEN `/api/auth/canvas-launch` receives a valid, signature/issuer/audience/expiry-checked, one-time-use (`jti`-tracked) JWT from the external Canvas Integration Service, THE SYSTEM SHALL mint a NextAuth session directly via `encode()` (bypassing the normal provider flow) with `isStudent:true`, all other roles `false`, an 8-hour cookie, and redirect to `/lesson/{personaId}`.
  - **Impersonation:** WHEN an admin POSTs `/api/auth/impersonate`, THE SYSTEM SHALL sign a 1-hour HMAC cookie bound to the admin's own hashed email and log a (console-only) audit event; every `jwt()` callback invocation thereafter re-verifies this cookie as the sole source of truth for the downgrade.
  - **Token refresh:** WHEN the AAD access token is expired, THE SYSTEM SHALL POST directly to Microsoft's token endpoint for a refresh; on failure, sets `session.error="RefreshAccessTokenError"` with no automatic sign-out.
* **Input Validation & Security Constraints:**
  - Canvas JWT: RS256/JWKS signature, issuer/audience pinned, required-claims check, atomic one-time-`jti` consumption (replay protection) — **in-memory cache backend does not share state across replicas; Redis is required in production but nothing enforces this at runtime.**
  - `personaId` regex-validated (`^[A-Za-z0-9_-]{1,128}$`) before use in a redirect path.
  - Impersonation cookie: HMAC-signed, constant-time-compared, expiry-checked, bound to the admin's own hashed identity, `__Host-` cookie prefix in production.
  - Open-redirect hardening rejects protocol-relative/cross-origin URLs in the NextAuth `redirect` callback.
  - No root `middleware.ts` exists; `(authenticated)` route-group session gating plus a separate `proxy.ts` (Next.js 16's middleware replacement) are the only global auth boundaries.
* **Exceptional Paths & Error Profiles:**
  - Missing/invalid Canvas token, persona ID, signature, claims, or replayed `jti` → 303 redirect to `/lti/error?code=<SPECIFIC_CODE>` (outside the authenticated route group, no session required).
  - `getCurrentUser()` with no session → throws raw `Error("User not found")`.
  - Impersonate POST without session/admin → 401/403 JSON.
  - `NEXTAUTH_SECRET` unset at Canvas-launch time → `SESSION_CREATE_FAILED` redirect rather than a crash.

### 3.2 Domain: Chat Core
* **Source Sub-directories:** `features/chat-page/` (chat-services, chat-input, artifacts, citation, tools), `app/(authenticated)/api/chat/`
* **Core Business Logic Actions:**
  - WHEN `/api/chat` is called, THE SYSTEM SHALL run `messageLimitPreflight` *before* creating/touching any thread or message record, so a blocked message leaves no trace.
  - WHEN the current thread's `version !== "v3"`, THE SYSTEM SHALL reject with 409 `THREAD_READ_ONLY` (legacy threads cannot be continued).
  - **Message limits:** two independently toggled admin caps — per-message char cap (default 4000, off) and daily message cap (default 100, off, reset at America/New_York midnight); WHEN a config/counter read fails, THE SYSTEM SHALL fail OPEN (never block a message due to infra failure).
  - **Prompt assembly:** base prompt + user name + timezone-aware date/time + persona system prompt + (if enabled) tool-usage instructions + (if `DataProduct` extension active) a data-product context block built server-side from the *thread's stored* data products — **client-supplied `dataProducts` in the request body are always discarded server-side** (anti-IDOR fix).
  - **PII redaction:** ON by default (`ENABLE_PII_REDACTION !== "false"`), applied only to user-authored text sent to the model — never to persisted history, assistant output, or documents.
  - **Tool orchestration:** `streamText()` with `maxRetries:5`, tool timeouts (30s default, per-tool overrides up to 150s) so a hung tool returns a "timed out" message to the model instead of hanging the whole stream; a 15-second keepalive ping prevents Azure App Service from killing idle long-running streams.
  - **Model access enforcement:** role → allow-list resolution (env override → Cosmos config → defaults → `"*"`); WHEN the requested model isn't in the caller's role allow-list, THE SYSTEM SHALL silently substitute the default model — this (plus each extension's own model allow-list/`requiresAdmin` check) is the **only non-bypassable gate**; all client-side extension visibility logic is explicitly documented as advisory only.
  - **Document ingestion:** upload → extract (Document Intelligence or plain text) → semantic chunk (~1000 tokens) → batch-embed → batch-write to Cosmos (100 ops/batch), running asynchronously via Next.js `after()` so the upload response returns immediately.
  - **Retrieval:** Cosmos-native hybrid search (`RANK RRF` of vector + full-text, 0.7/0.3 weights by default), falling back to vector-only when no usable keywords exist.
  - **PDF export:** WHEN the exporting user `isStudent`, THE SYSTEM SHALL strip any message part tagged `containsCanvasStudentData` before rendering.
* **Input Validation & Security Constraints:**
  - Anthropic images >5MB rejected pre-flight (400 `IMAGE_TOO_LARGE`) to avoid a hard provider-side failure.
  - CSV export: values starting with `= + - @` or control characters are quote-prefixed to defeat spreadsheet formula/DDE injection.
  - SVG artifacts: DOMPurify SVG profile + forbidden tags/attrs + URI-scheme allow-list (http/https/mailto/tel/relative only).
  - HTML artifacts render in a sandboxed iframe (`allow-scripts` only, no `allow-same-origin`); **React artifacts execute via `react-live` in the same JS realm as the app — not a real sandbox**, despite design docs describing one.
  - Shared-thread access (`GetSharedThread`) checks only "is the caller logged in," not "is the caller the original recipient" — any authenticated holder of a `shareId` can view/clone it, and shares never expire.
* **Exceptional Paths & Error Profiles:**
  - Char/daily cap exceeded → 400 `MESSAGE_TOO_LONG` / 402 `DAILY_MESSAGE_LIMIT_EXCEEDED` (with `resetsAt`).
  - Any unhandled `ChatAPIEntry`/`executeAIStream` exception → generic 500, regression-tested to never leak internal error detail to the client.
  - Tool-specific failure handling is inconsistent: Calculator/Weather let exceptions propagate raw; Map/DataProduct/CSV/ImageGen catch and return a structured `{success:false}` payload instead.

### 3.3 Domain: Multi-Chat / Chat-Home / Lesson Mode
* **Source Sub-directories:** `features/multi-chat-page/`, `features/chat-home-page/`, `features/lesson-chat/`, `features/lesson-mode/`
* **Core Business Logic Actions:**
  - Multi-chat: WHEN a user sends the first message in an empty quadrant, THE SYSTEM SHALL create a thread on demand (`/api/multi-chat/thread`) before sending; quadrant count floors at 2 (removing below 2 clears the persona instead of deleting the slot) and caps at 4. Multi-chat state is **entirely client-side (`useState`) — nothing is persisted**, so a page refresh loses all quadrant/persona/thread associations.
  - Chat-home: WHEN the page mounts, THE SYSTEM SHALL show only the user's starred (favorite) personas; empty state prompts starring from the main Assistants page.
  - Lesson mode: WHEN a path starts with `/lesson/`, THE SYSTEM SHALL derive lesson context from the URL, look up the persona via a cross-account-capable `FindPersonaByID` (not the normal per-user listing, since the persona may be instructor-owned), gate all non-lesson UI (voice, extra menu items), and force the URL to `/lesson/{personaId}/{threadId}` once a thread exists.
  - WHEN a lesson thread's stored `personaId` doesn't match the URL persona, THE SYSTEM SHALL redirect back to `/lesson/{personaId}` (prevents cross-lesson thread access via URL tampering).
  - **Canvas submission:** WHEN a student submits lesson work, THE SYSTEM SHALL derive all Canvas identity exclusively from the server-side session (never the request body), generate a PDF, POST it to the external Canvas Integration Service (Azure-AD service-to-service auth, retried up to 4 times on 429/5xx/network errors, capped at ~37.5MB), and best-effort write a submission audit record regardless of outcome.
* **Input Validation & Security Constraints:**
  - `submit-lesson` hard-gates on `isStudentUser` and requires an active Canvas launch context; a non-Canvas-launched student session cannot submit.
  - Multi-chat's thread-creation route has no independent persona-ownership check (relies on the persona already being scoped to the caller by `FindAllPersonaForCurrentUser`).
* **Exceptional Paths & Error Profiles:**
  - Missing Canvas context → 422 `CANVAS_CONTEXT_MISSING`; expired launch token → 422 `CANVAS_TOKEN_EXPIRED` (best-effort unverified decode; parse failure fails open); upstream failures classified into `NOT_ENROLLED`/`ASSIGNMENT_CLOSED`/`FILE_TOO_LARGE`/`CANVAS_UNREACHABLE`/`UNKNOWN` by status+message-substring sniffing (brittle coupling to the external service's free-text wording).
  - Multi-chat: if thread creation fails mid-send, the typed message and any attachments are silently dropped (only console-logged) — no user-facing error.

### 3.4 Domain: Canvas LTI Integration & Student View
* **Source Sub-directories:** `features/canvas/`, `features/canvas-integration/`, `features/student-view/`, `app/lti/`, `app/(authenticated)/api/canvas/`
* **Core Business Logic Actions:**
  - This app never validates raw LTI 1.3 launches itself — an external "Canvas Integration Service" does that and hands off a custom signed JWT, which this app verifies and turns into a NextAuth session (see §3.1).
  - A **separate** Canvas OAuth flow (`/api/canvas/oauth/*`) exists purely to power in-chat data-access tools (grades, assignments, courses via 16 read-only MCP tools) — tokens are AES-256-GCM encrypted at rest and proactively refreshed 5 minutes before expiry.
  - WHEN Canvas MCP tool output is about to reach the LLM, THE SYSTEM SHALL apply a three-tier PII redaction pass (always-redact / peer-only-redact / always-strip-auth-fields), failing closed (treats everyone as a "peer," i.e. redacts, if the current user ID is missing).
  - Student View: WHEN an admin enters Student View, THE SYSTEM SHALL sign a 1-hour impersonation cookie and force `isStudent:true` in both the `jwt()` and `session()` callbacks (defense in depth); exiting requires a hard navigation so server components re-derive the un-downgraded role.
* **Input Validation & Security Constraints:**
  - No roster/list-students MCP tool exists **by design** (FERPA).
  - Canvas identity for submission is always session-derived, never client-supplied (explicit anti-forgery fix, regression-tested).
  - Session `sub` is `hash(canvas_env:canvas_user_id)` specifically to prevent numeric-ID collisions across the three Canvas tenants (ECPI/NTT/SkillOps) sharing one Cosmos partition space.
* **Exceptional Paths & Error Profiles:**
  - `/lti/error` (outside the authenticated route group) renders both this app's own error codes and the external Integration Service's own redirect-in error codes; special-cases assignment-not-found/lookup-failed with instructor-facing remediation copy.
  - `/api/canvas/diagnose` has **no explicit auth guard of its own** — it relies transitively on an internal helper throwing if unauthenticated, unlike every other route in this domain which checks the session explicitly.

### 3.5 Domain: Persona Management & Persona Studio
* **Source Sub-directories:** `features/persona-page/`, `features/persona-studio/`, `app/(authenticated)/persona*/`, `app/api/personas/`
* **Core Business Logic Actions:**
  - **`EnsurePersonaOperation`** (the central gate): grants access to admins, the owner, hashed collaborators, or — read-only — any student when `isLessonPersona === true`; otherwise returns a deliberately non-revealing `UNAUTHORIZED` ("not found") to prevent persona-ID enumeration.
  - WHEN a non-admin attempts to delete or write to a lesson persona, THE SYSTEM SHALL block it even if the read-access gate above would otherwise pass.
  - **Privilege-escalation guard:** on update, a non-admin's submitted `isLessonPersona` value is discarded and the existing flag is preserved server-side, regardless of payload content.
  - **Ownership transfer:** implemented as delete-then-recreate under the new owner's partition key (Cosmos partition key = `userId`) — not transactional; a failure after the delete permanently loses the persona.
  - **AI-assisted builder** (`/api/persona-builder`): structured-output generation (`generateObject`) that only emits fields it's confident about (omitted = "leave unchanged"), never fabricates a persona name, and picks a model recommendation from an allow-listed catalog (Opus models explicitly excluded from AI recommendations, though a human can still pick one manually).
  - **Live preview** (`/api/persona-preview`): streams a real chat completion against the *draft* (unsaved) persona config, with the same tools actually executing live — not mocked, despite the system prompt telling the model otherwise.
  - Non-admin persona listings never include lesson personas — they're reachable only via a direct Canvas/LTI deep link, not the general catalog.
* **Input Validation & Security Constraints:**
  - Sharing targets are role-gated (`getSharingPermissions`) — admins can share with groups, everyone else only with individuals by default, subject to company-wide overrides.
  - `apiKey` (A2A credential) is stripped by the "public" persona accessor, but the raw (non-stripped) accessor is still directly imported by at least one client component — the protection is opt-in per call site, not structurally enforced.
  - `dataProducts`-required-when-`DataProduct`-extension is enforced in three separate UI call sites but **not** in the shared Zod schema — a direct API call can bypass it.
* **Exceptional Paths & Error Profiles:**
  - Unauthorized access/edit → `UNAUTHORIZED`, "Persona not found with id: {id}" (ambiguous by design).
  - Persona Studio edit page for a lesson persona viewed by a non-admin → friendly `DisplayError`, not a hard failure.
  - Persona-builder/preview: structured 400/500 JSON errors for invalid payload, missing conversation turns, no compatible model, or generation failure — with content-filter/rate-limit/network failure classification logged for the builder.

### 3.6 Domain: Orchestration & Agents (A2A)
* **Source Sub-directories:** `features/orchestration-page/`, `features/agents/`, `app/(authenticated)/orchestration/`, `app/api/agents/`
* **Core Business Logic Actions:**
  - WHEN an orchestration is saved with ≥1 node, THE SYSTEM SHALL require at least one `trigger` and one `terminal` node and a single connected graph component (BFS reachability) — **cycle detection is not performed at any layer**.
  - Execution: locate the trigger → follow its *first* outgoing connection only (no branching/fan-out support despite the schema modeling a `condition` field on connections) → require the next node be `persona` → recursively delegate → resolve the *first* terminal node found → mark completed.
  - **Delegation guards:** a hard depth limit (5) short-circuits with zero I/O before it's reached; a delegation chain that would revisit an already-visited node is refused as circular — both return graceful non-throwing results, not execution failures.
  - **A2A invocation** (`/api/agents/[id]`): requires header `x-agent-api-key`, constant-time-compared against the persona's own `apiKey` (401/404/403 fail-closed for missing key/persona/`a2aEnabled`); the AI-memory `userId` is derived from the *caller's context ID*, not the persona ID, to prevent cross-caller memory bleed when multiple external callers reuse the same persona-as-agent.
  - Production execution is fully delegated to external Azure Logic Apps via an authenticated webhook trigger; **the actual step-engine that runs there is not present in this repository** — only the trigger/cancel HTTP calls exist here. Local dev execution runs the engine in-process instead.
* **Input Validation & Security Constraints:**
  - `EnsureOrchestrationOperation` has a **no-op authorization bug**: if the caller is neither admin nor owner, the function falls through and returns the same "OK" response anyway — every downstream caller (start/edit/delete) trusts this as an authorization signal. In practice this is likely masked by Cosmos partition-key scoping (lookups are scoped to the caller's own partition), but the in-code check itself does not enforce anything.
  - Graph-shape validation is server-side; per-node-type field validation (e.g., a trigger node needs a `triggerType`) exists only client-side and is not re-run on direct API submission.
* **Exceptional Paths & Error Profiles:**
  - Missing trigger/disconnected node/unresolvable persona → execution marked `failed` with structured `errors[]` entries including stack detail.
  - **The workflow-builder UI is not functional end-to-end in this snapshot**: the "new orchestration" save path never attaches nodes/connections to the create call (canvas ref not wired up — anything drawn is silently discarded), and the "view existing orchestration" `[id]` page renders an empty fragment — despite the execution engine underneath being fully implemented and unit-tested.

### 3.7 Domain: Data Products
* **Source Sub-directories:** `features/dataproducts-page/`, `app/(authenticated)/dataproduct/`, `app/api/dataproduct/`
* **Core Business Logic Actions:**
  - CRUD is schema-validated (non-empty name/description) before persistence; delete is soft-delete only.
  - Visibility union: owner + collaborators + `sharedWith` (individual emails or `@employees`/`@contractors`/`@{organizationId}` group tokens) — there is **no `@students`/`isStudent` sharing branch**.
  - **Edit vs. delete access differ:** collaborators can edit but explicitly cannot delete (owner/admin only for delete).
  - File upload requires edit access; extraction failures during upload are swallowed and the upload still reports success.
  - **External MCP endpoint** (`/api/dataproduct/[id]/[transport]`): API-key-gated (constant-time compare), but **all business failures (missing product, MCP disabled, bad key, tool error) are returned as HTTP 200 with MCP text content** — never a non-2xx status; differentiated only by message text.
  - **Ingestion pipeline** (Azure Logic Apps, external to this repo's TypeScript, defined only as `workflow.json` + Cosmos-container specs): a Delta Walker polls Microsoft Graph for changes on a recurrence schedule, a Job Processor extracts/chunks/embeds and writes vector rows, a daily Cleanup job soft-deletes stale rows, and an HTTP-triggered Blob Backfill job exists behind a feature flag. **No code in this repository ever invokes these Logic Apps** — they run autonomously on Azure-internal schedules.
* **Input Validation & Security Constraints:**
  - MCP API key: length-checked then `timingSafeEqual`-compared.
  - **Gap:** URL-entry edit/delete functions (`DeleteDataProductURL`/`UpdateDataProductURL`) perform **no authorization check at all** before mutating — any authenticated caller with a valid ID can edit/delete another user's URL entry.
  - Four separate, subtly divergent implementations of "can this user act on this data product" exist across the codebase (plain-email-comparison variants vs. a hashed-comparison variant).
* **Exceptional Paths & Error Profiles:**
  - `GetDataProductFile` breaks the app-wide `ServerActionResponse` convention, returning raw plain-text `Response` objects instead.
  - Health/rate-limit/audit checks fail OPEN on any internal error (treat as available/allowed rather than blocking).
  - Logic-Apps retry/dead-letter fields (`retryCount`, `deadletters` container) are fully specified in the schema/docs but never actually written to by any workflow — retry/DLQ semantics are aspirational, not implemented.

### 3.8 Domain: Admin / Settings / Executive Reporting
* **Source Sub-directories:** `features/admin/`, `features/admin-settings/`, `features/executive-page/`, `features/user-preferences/`
* **Core Business Logic Actions:**
  - Admin-only gate (`isAdmin` check before touching Cosmos) is consistently applied across system-config, model-config, message-limit, and persona-generation-model mutation services.
  - Model deletion is always soft (`isDeleted`), never a hard remove; model *read* access for chat is computed by intersecting `isEnabled`, the caller's role allow-list, and (if the model `requiresAdvancedModelAccess`) the caller's `advancedModelAccess` flag.
  - Message-limit and persona-generation-model *read* paths are intentionally **not** admin-gated (any user can read the effective config so enforcement works app-wide) while the *write*/admin-UI paths are admin-gated — an explicitly documented asymmetry.
  - Executive dashboard: gated to `isAdmin OR advancedModelAccess`; served from a Cosmos-cached document, recomputed via several sequential (artificially delayed, to avoid Cosmos rate limits) queries when stale, with growth percentages clamped to 0 rather than shown negative.
* **Input Validation & Security Constraints:**
  - Message-limit caps are re-validated server-side as integers ≥1 regardless of client-side validation ("never trust the client; a 0 cap would block everyone").
  - Persona-generation model selection is restricted to a pre-filtered allow-list even if a broader model exists in the general registry.
  - `/api/user/preferences/*` routes have **no explicit session check of their own** — they rely on an internal helper throwing if unauthenticated, unlike the explicit checks used on every admin route (inconsistent auth-failure shape: 500 instead of 401).
* **Exceptional Paths & Error Profiles:**
  - Executive stats: on any query error, the service *masks the failure* and returns `status:"OK"` with hardcoded placeholder numbers; the page component has its own **second, different** hardcoded fallback for the same failure mode — two inconsistent "it broke" datasets for one condition.
  - A fully-specified, larger `ExecutiveDashboardStats` type (hockey-stick growth, team comparisons, board export) exists with **zero implementing service** — dead/aspirational design.

### 3.9 Domain: Prompt Library
* **Source Sub-directories:** `features/prompt-page/`, `app/api/prompts/`, `app/(authenticated)/api/promptGenerator/`
* **Core Business Logic Actions:**
  - `EnsurePromptOperation` grants write access to admin/owner/collaborator only; read access additionally includes anyone the prompt is `sharedWith` (individual or group).
  - Ownership transfer, like personas, is implemented as delete-then-recreate under the new owner's partition key — not transactional, with no rollback if the recreate fails.
  - AI-assisted generation (`/api/promptGenerator`) wraps user input in a large fixed prompt-engineering meta-prompt, calling a primary model with a one-shot fallback to a second model on failure.
  - Selecting a saved prompt in chat input copies its `description` field verbatim into the textarea — **no variable/placeholder substitution exists**; `[bracket]` conventions in seed content are purely cosmetic.
* **Input Validation & Security Constraints:**
  - `name`/`description` required non-empty; sharing targets role-gated identically to personas.
  - **`TransferPromptOwnerShip` trusts client-supplied JSON** for `name`/`description`/`createdAt`/`sharedWith` rather than re-deriving from the server-verified record — only ownership (owner/admin) is actually checked, field values themselves are not re-validated on this path.
* **Exceptional Paths & Error Profiles:**
  - `EnsurePromptOperation` failure returns a uniform ambiguous `UNAUTHORIZED` message regardless of true cause (not-found vs. forbidden).
  - Prompt-generator total failure (primary + fallback model both fail) returns a plain-text 500 body, inconsistent with the JSON content-type of the success path.

### 3.10 Domain: Common Cross-Cutting Services
* **Source Sub-directories:** `features/common/database/`, `features/common/storage/`, `features/common/document-processing/`, `features/common/file-upload/`, `features/common/sharing/`, `features/common/ai/`, `features/pii-redaction/`, `lib/`
* **Core Business Logic Actions:**
  - `IDatabaseProvider.deleteItem` is always a soft delete; `incrementField` lazily creates a seeded document on first use and retries through concurrent-create races.
  - File uploads validate, in strict order: presence → extension allow-list → size (≤100MB) → a processing-readiness hook; only then is the buffer read.
  - Blob storage always returns an **application-proxied URL**, never a raw Azure blob URL, to the client.
  - PII redaction runs an ML pass first (placeholder-mode detection, restoring the original value for any detection type not on an explicit allow-list, to avoid false positives like misclassifying "Microsoft Teams" as a person name), then unconditionally also runs five fallback regexes (SSN, phone, student-ID format, DOB, street address) regardless of ML success — a deliberate fail-closed design.
  - Sharing-permission resolution maps `isAdmin→ADMIN`, `isEmployee→FACULTY`, `isContractor→STUDENT`, with a "STAFF" role case in the switch statement that is provably unreachable (never produced by the mapping function).
* **Input Validation & Security Constraints:**
  - URL validation uses WHATWG `URL`-parsed hostname suffix comparison specifically to defeat substring-based host-spoofing (`evil.com/host.com`, `host.com.evil.com`).
  - Content-Disposition header building strips CR/LF (header-injection defense) and escapes quotes/RFC-5987-encodes non-ASCII filenames.
  - Graph/OData filter construction validates email format/length and doubles single quotes before interpolation (OData injection defense).
  - HTML-stripping sanitization re-applies its tag-removal regex until stable, specifically to defeat nested-tag-collapse bypass attacks.
* **Exceptional Paths & Error Profiles:**
  - All Cosmos provider errors are wrapped into the standard `{status:"ERROR"}` envelope — no exceptions escape the provider class; `healthCheck()` is a notable exception, returning `status:"OK"` even when the wrapped payload reports `unhealthy` (infra failure modeled as a healthy envelope around an unhealthy payload).
  - PII ML-detection failure is caught and logged; redaction proceeds using only the fallback regexes rather than failing the request.

### 3.11 Domain: Support Features
* **Source Sub-directories:** `features/feedback/`, `features/changelog-page/`, `features/version-alert/`, `features/main-menu/`, `app/health/`, `app/api/health/`
* **Core Business Logic Actions:**
  - Feedback is a **pure proxy, never persisted locally** — validated, ownership-checked against the caller's own thread, then forwarded to an external ECPI Feedback API.
  - Version-alert: WHEN the app mounts, THE SYSTEM SHALL compare the latest changelog version against the user's persisted acknowledgment and a fixed 60-day "within alert period" window; acknowledging is optimistic-then-persisted, with automatic revert on a failed DB write.
  - Readiness probe checks both Cosmos DB and Azure Key Vault (each under a 5s timeout, in parallel); liveness probe checks Cosmos DB only.
  - Main menu: admin users see a "View as Student" toggle that drives the Student-View impersonation flow described in §3.4.
* **Input Validation & Security Constraints:**
  - Feedback submission requires an authenticated session and verifies the referenced thread belongs to the caller before forwarding.
  - Health endpoints are intentionally unauthenticated (for orchestrator probing) but do leak raw dependency error strings to any caller.
* **Exceptional Paths & Error Profiles:**
  - Feedback proxy failure (external API unreachable/misconfigured) is treated as non-critical client-side — logged only, never shown to the user, to avoid disrupting the learning experience.
  - **The changelog source directory has been removed from the repository entirely**, yet the changelog-reading function has no existence check or try/catch around it — `/changelog` and version-alert's "latest version" lookup would fail at runtime unless the directory is repopulated out-of-band at deploy time.

---

## 4. Cross-Domain Integrations & System Dependencies

| Source Component | Target Dependency | Communication Contract | Implicit Expectation |
|---|---|---|---|
| Auth (`auth-api.ts`) | Azure AD OAuth 2.0 / Microsoft identity platform | Synchronous OAuth token endpoint + JWT session | AAD app manifest has `groupMembershipClaims:"SecurityGroup"` configured; four group GUIDs map to roles |
| Canvas launch (`canvas-launch-service.ts`) | External "Canvas Integration Service" (separate Azure Function, owns real LTI 1.3 validation) | Signed custom JWT + remote JWKS endpoint | The external service has already validated the true LTI 1.3 launch; this app trusts its signature alone |
| Lesson submission | External Canvas Integration Service `/canvas/submit` | Sync REST, Azure-AD service-to-service bearer auth | Upstream returns `{submission_id, canvas_url,...}` or a typed error; retried on 5xx/429/network |
| Canvas chat tools | Canvas Student Context MCP server (separate service) + native Canvas LMS OAuth | MCP tool calls / OAuth2 | Standard Canvas REST field shapes (`workflow_state`, etc.); PII redaction applied before the LLM sees output |
| Chat Core | Vercel AI SDK `streamText` → model registry (`lib/ai-config.ts`) → Anthropic/OpenAI/Google/Azure Foundry/Azure OSS | Streaming, provider-specific `providerOptions` | Anthropic hard-rejects images >5MB; Azure App Service resets idle HTTP/2 streams (~60s) |
| Chat Core (document search) | Azure Cosmos DB vector + full-text hybrid search | Sync SQL/RRF query | Vector index configured per the container's vector policy; only one DiskANN index per container |
| Data Products / Chat tools | External MCP endpoint (`/api/dataproduct/[id]/[transport]`) | Streamable-HTTP MCP, API-key auth | Callers hold a valid `apiKey`; business failures still return HTTP 200 |
| Data Products ingestion | Azure Logic Apps (external, not in this repo) + Microsoft Graph `/delta` + Azure Document Intelligence + Azure OpenAI embeddings | Async, schedule-triggered webhook/recurrence | Delta Walker/Job Processor run independently of any in-app trigger; no code here invokes them |
| Orchestration execution (prod) | Azure Logic Apps (external step engine, not in this repo) | Async webhook trigger/cancel, Bearer-token authed | The actual workflow engine's execution semantics are opaque to this codebase — only the trigger/cancel calls exist here |
| Persona A2A / Agents | External A2A callers (JSON-RPC over HTTP) | Sync JSON-RPC, `x-agent-api-key` header auth | Caller reuses `contextId` consistently so per-caller memory isolation holds |
| Common Services | Azure Blob Storage, Azure Cosmos DB, Azure Document Intelligence, Microsoft Graph, Azure Key Vault (health only) | Sync SDK calls, `DefaultAzureCredential`/managed identity | Managed-identity credentials are correctly provisioned in all deployed environments |
| Common Services (optional) | Google Vertex AI | Workload Identity Federation chain (Azure MI → GCP STS → SA impersonation) | All 6 required env vars are set together; otherwise the provider is silently omitted |
| Feedback | External ECPI Feedback API | Sync REST, optional Entra service token | Feedback is never retried/persisted locally on failure — fire-and-forget from the app's perspective |
| WebSocketProvider (chat + data product display) | **Hardcoded AWS API Gateway WebSocket endpoint** | `wss://` | Contradicts the "Azure-only" architecture stated in project docs — this is a live, non-dead dependency |

---

## 5. Architectural Debt & Convergence Targets

* **Incomplete AWS-to-Azure migration, not just comments:** Despite documentation stating AWS paths were removed, live, executed code still depends on AWS-era constructs — `generateS3Uri`/Bedrock config helpers are actively called during file upload; document/chat models retain `s3ObjectKey`, `bedrockDocumentId`, SQS `deletionMessageId`, and ingestion-status enum values that only make sense for an AWS pipeline; a document-delete service explicitly branches on "AWS vs Azure" routing; and a WebSocket provider's default endpoint is a live AWS API Gateway URL. **Convergence target:** delete all S3/Bedrock/SQS-shaped fields and helper functions, and replace the hardcoded AWS WebSocket URL with an Azure-native equivalent or a required, unconditional prop.

* **Duplicated/competing implementations of the same business logic:** two full parallel chat-completion pipelines exist for orchestration (`SimplifiedPersonaExecutor`, live vs. `PersonaStreamExecutor`/`orchestration-tool-service.ts`, dead); two Canvas-submission routes with divergent error-handling depth; two document-text-extraction abstraction layers wrapping the same underlying Document Intelligence client; two independent Azure Blob storage client implementations; two hardcoded "executive stats failed to load" fallback datasets. **Convergence target:** pick one implementation per concern and delete the other; add a lint/architecture rule against parallel service files with overlapping responsibility.

* **Authorization no-ops and gaps:** `EnsureOrchestrationOperation` falls through to an "OK" response even when the caller is neither owner nor admin; `DeleteDataProductURL`/`UpdateDataProductURL` perform no authorization check at all; `/api/canvas/diagnose` and `/api/user/preferences/*` rely on an internal helper throwing rather than an explicit session check. **Convergence target:** standardize on one explicit `requireAuth()`/`requireRole()` guard used at the top of every mutating handler, and add a static-analysis or test-coverage rule that every Server Action touching another user's data must assert ownership before writing.

* **Non-functional or broken UI flows shipped alongside fully-implemented backends:** the orchestration workflow-builder's "create" flow never persists drawn nodes/connections, and its "view existing" page renders an empty fragment, even though the execution engine underneath is complete and unit-tested; the changelog feature's source directory has been deleted from the repository while the reader code has no guard against its absence. **Convergence target:** treat these as release blockers, not documentation cleanup — either wire up the missing UI/data path or remove the dead engine code and feature entry points together.

* **Silent failure-masking in reporting paths:** the executive dashboard converts real query failures into a fake "OK" status with hardcoded numbers (two different hardcoded sets, in two different files, for the same failure). **Convergence target:** propagate a genuine error state to the UI with a visible "data unavailable" indicator instead of fabricating plausible-looking metrics.

* **Inconsistent data-access patterns for structurally similar config documents:** some admin/config services go through the generic `IDatabaseProvider`, others hit `HistoryContainer()`/`ConfigContainer()` with raw Cosmos SQL directly for the same category of singleton/per-user document. **Convergence target:** funnel all config/singleton reads through one repository-style helper so query semantics (soft-delete handling, container routing) don't drift.

* **Weak or absent contract-level validation at the API boundary:** node/connection configs, prompt/persona field-length caps (e.g., Persona Studio's 80/1000-char limits), and the data-product-requires-`DataProduct`-extension rule are all enforced only in specific UI code paths, not in the shared Zod schemas — any direct API caller (or a future UI entry point) can bypass them. **Convergence target:** move these rules into the shared schema layer so every entry point inherits them for free.

* **Operational/runtime fragility masked by comments rather than guarded in code:** in-memory JTI-replay and rate-limit caches silently degrade to per-instance-only correctness in a multi-replica deployment unless Redis is explicitly configured, with no startup check or warning; Logic Apps' documented retry/dead-letter mechanism (`retryCount`, `deadletters` container) is fully schema'd but never actually written to by any workflow, so failed ingestion jobs are effectively dropped with no recovery path. **Convergence target:** add a startup assertion that fails fast when `CACHE_PROVIDER`/`REDIS_URL` is unset in a multi-instance-capable environment, and implement the dead-letter/retry path the schema already promises.

* **Verbose, inconsistent production logging:** emoji-prefixed and step-by-step `console.log`/`console.error` debug instrumentation (including partial tokens, hashed IDs, and emails) is left in numerous production code paths (Canvas launch, persona services, prompt ownership transfer, orchestration executors) alongside a more disciplined structured `logger`/telemetry system used elsewhere. **Convergence target:** route everything through the structured logger with a consistent debug-gate, and strip identifier/token fragments from log lines.

* **Oversized, multi-concern service files:** `persona-service.ts` (~1000+ lines), `chat-api.ts`'s `ChatAPIEntry` (~800 lines, including ~180 lines of dead commented-out code), and `cosmos-document-service.ts` (~730 lines) each mix CRUD, authorization, cross-partition query fallbacks, and side-effecting business logic in one file. **Convergence target:** split along the seams already implied by the EARS statements above (authorization / CRUD / cross-partition query / side-effect orchestration) as part of any rebuild.

