<!--
Sync Impact Report
Version change: 1.1.0 → 1.2.0 (MINOR — new Technology Stack & Platform Alignment section added)
Modified principles: none (existing principles unchanged)
Added sections: Technology Stack & Platform Alignment
Removed sections: none
Templates requiring updates:
  - .specify/templates/plan-template.md ⚠ pending — each feature's "Technical Context" in plan.md should
    default to this section's stack rather than re-deciding it; no template edit needed, but authors of
    /speckit-plan runs must read this section before filling Technical Context in.
  - .specify/templates/spec-template.md ✅ no changes needed — specs remain tech-agnostic by design
  - .specify/templates/tasks-template.md ✅ no changes needed — unaffected by this amendment
Follow-up TODOs:
  - This section's specific product names/versions reflect knowledge current to ~January 2026 and were
    NOT re-verified against a live web search at amendment time (deep-research was started and then
    stopped by the user, who asked to proceed from existing knowledge instead). Re-validate against
    Microsoft Learn / Azure Architecture Center before the first /speckit-plan locks in package versions.
-->

# Enterprise AI Platform Constitution

## Core Principles

### I. Azure-Only, No AWS Vestiges
The system targets Azure exclusively (Cosmos DB, Blob Storage, Document Intelligence, Key Vault,
Azure Foundry/OSS model endpoints). No AWS-shaped code — S3 URIs, Bedrock config, SQS message
IDs, Lambda-oriented status enums, or hardcoded AWS endpoints — may be introduced or extended.
Existing AWS-era code is tracked debt to remove, never a precedent to build on.
Rationale: `docs/SSD_Document.md` §5 documents live, *executed* AWS-era code (S3 URI helpers,
Bedrock config, a hardcoded AWS WebSocket endpoint) despite project docs claiming the migration
was complete — a half-finished migration that nobody is actively removing rots silently.

### II. Explicit, Server-Side Authorization for Every Access
Every handler that mutates another user's data MUST assert ownership/role explicitly, before the
write, through one canonical authorization check per resource type. A check that can fall through
to a success response on an unauthorized or unauthenticated path is a defect, not an acceptable
default — it must fail closed. This applies equally beyond mutations: read access, tool
availability, and model/entitlement selection MUST all be resolved server-side; any client-side
gating (UI visibility, disabled buttons) is advisory UX only and is never itself a security
boundary.
Rationale: real bugs already found and being fixed via this project's specs trace back to this
rule being optional rather than structural: `EnsureOrchestrationOperation`'s no-op fallthrough
(spec 001), `DeleteDataProductURL`/`UpdateDataProductURL` having no check at all (spec 012), and
`TransferPromptOwnerShip` trusting client-supplied fields instead of the server record (spec 016).
`docs/PRODUCT_REQUIREMENTS_DOCUMENT.md` REQ-NFR-SEC-1, REQ-TOOL-2, and REQ-MODEL-2 independently
state the same rule for the new platform — every access decision (not only writes) is resolved
server-side, with UI gating treated as advisory only.

### III. Fail Loud, Never Fabricate Success
When an internal error occurs, the system MUST surface a genuine error or degraded state — never
mask it behind a fabricated "OK" status or hardcoded placeholder data. Fail-open is acceptable only
where explicitly chosen as a documented availability policy (e.g., message-limit config reads, PII
ML-detection fallback to regex); silently faking success is never acceptable.
Rationale: the executive dashboard's two divergent hardcoded "it broke" fallbacks (spec 015) and
document-extraction failures silently reported as upload success (spec 005/013) both hid real
outages from users and operators instead of surfacing them.
`docs/PRODUCT_REQUIREMENTS_DOCUMENT.md` REQ-NFR-REL-1 is the concrete instance of this principle's
fail-open carve-out: optional dependencies (cache, legacy search, telemetry) MAY fail open without
breaking core chat, precisely because that policy is explicit and documented, not silent.

### IV. One Implementation Per Concern
Business logic for a given concern — an authorization check, an extraction pipeline, a storage
client, an error-fallback dataset — MUST have exactly one implementation. Discovering a second,
divergent implementation during development is a signal to consolidate immediately, not to add a
third alongside it.
Rationale: `docs/SSD_Document.md` §5 found four divergent "can this user act on this resource"
checks, two parallel orchestration executors, two blob storage clients, and two document-extraction
abstraction layers — all already drifting independently from one another.

### V. Schema-Enforced, Not UI-Enforced, Validation
Business rules that gate what data may be persisted (required fields, cross-field dependencies)
MUST be enforced in the shared schema layer, not solely in UI call sites. A direct API caller is
bound by the same rules a UI user is, with no exceptions.
Rationale: documented bypasses — the persona `dataProducts`-required-when-extension-selected rule,
and persona/prompt field-length caps — existed only in UI code and were trivially bypassable via a
direct API call.

### VI. Testable, EARS-Style Requirements
Every feature spec's functional requirements are written in falsifiable "System MUST ..." form,
with an independent test defined for each user story, so a requirement can be verified without
reading the implementation. A requirement that only describes current (possibly buggy) behavior
is not acceptable — specs state the fixed, to-be behavior; the bug is context, not the requirement.
Rationale: this is the format `docs/SSD_Document.md` already uses and that every spec under
`specs/` was deliberately reframed into (see `docs/spec-kit-decomposition-plan.md`'s reframing
rule) — consistency here is what makes as-is discovery facts and to-be specs comparable.

## Technology Stack & Platform Alignment

> **Currency caveat**: the product names, versions, and GA/preview status below reflect the
> assistant's training data as of ~January 2026, not a live-verified snapshot — a deep-research
> pass was intentionally skipped per user instruction. Microsoft ships major updates on roughly a
> Build (May) / Ignite (November) cadence, so before the first `/speckit-plan` locks in specific
> package versions, NuGet/PyPI package names, or API shapes, re-verify current GA status against
> Microsoft Learn and the Azure Architecture Center. Treat this section as directionally binding
> (the platform choices), not version-pinned.

- **UI**: Blazor Web App on the latest .NET LTS, using the **Interactive Server** render mode as
  the default for chat/agent surfaces — a persistent SignalR connection is the natural fit for
  token-by-token streaming responses, keeps prompt-assembly/authorization logic server-side (never
  shipped to the client, per Principle II), and avoids a large WebAssembly payload for an
  internal/enterprise line-of-business app. Static Server rendering is acceptable for genuinely
  static/non-interactive pages (e.g., the changelog); **Auto** render mode (Server first load,
  WebAssembly thereafter) is an acceptable alternative for latency-sensitive, less
  security-sensitive surfaces (e.g., persona/prompt browsing) if profiling justifies it — but is
  not the default, and any such deviation must be recorded per this constitution's Governance
  section rather than adopted silently.
- **Languages**: C# (.NET) is the primary language for API, orchestration, and business-logic
  code. Python is permitted specifically where its ecosystem has a clear, material advantage —
  data-science/ML-pipeline code (embedding evaluation, offline scoring, notebook-derived analysis)
  — not as a default choice; a Python component MUST still integrate through the same
  server-side-authorization and schema-validation boundaries (Principles II, V) as C# code, not
  bypass them via a separate trust zone.
- **Agent & orchestration layer**: Microsoft Agent Framework (the unified successor to Semantic
  Kernel and AutoGen) is the in-process library for building personas/agents, tool/function
  calling, and multi-agent orchestration (replacing this spec set's `OrchestrationModel` graph
  executor and the reference implementation's bespoke `SimplifiedPersonaExecutor`). Azure AI
  Foundry is the unified control plane for model catalog, deployment, and evaluation; **Azure AI
  Foundry Agent Service** is the managed hosting/runtime for agent threads, state, and tool
  invocation; **Foundry Hosted Agents** is the deployment target for custom agent code built on
  Microsoft Agent Framework, replacing the reference implementation's external-Logic-Apps
  execution delegation (SSD_Document.md §3.6) with a first-party managed runtime.
- **Data & storage** (Azure-only, per Principle I): **Azure AI Search** for RAG retrieval (hybrid
  vector + keyword, replacing the reference implementation's Cosmos-native RRF hybrid search) —
  Microsoft's purpose-built retrieval service is preferred over rolling hybrid search inside a
  general-purpose document store. **Azure Cosmos DB** remains appropriate for high-throughput,
  flexible-schema chat/message/thread storage where the domain genuinely benefits from a
  document model. **Azure SQL Database** (via EF Core) is preferred over Cosmos DB for strongly
  relational, schema-stable entities (personas, prompts, sharing policy, admin/system config) —
  a better fit for a C#/.NET-first stack and for the schema-enforced validation Principle V
  requires. **Azure Blob Storage** for files and generated images. **Azure Cache for Redis** for
  distributed cache, rate limiting, and the JTI-replay cache — directly closing the multi-instance
  cache gap spec 003 flagged in the reference implementation. **Azure Key Vault** for secrets.
  **Microsoft Entra ID** (formerly Azure AD) for enterprise SSO, consistent with the existing
  OAuth/OIDC approach this spec set already assumes.
- **Well-Architected Framework alignment**: every `/speckit-plan` MUST address the five WAF
  pillars explicitly for AI/agentic workloads, not just traditional web-app concerns — Reliability
  (timeouts/retries/circuit-breakers around model and tool calls, per the pattern already specified
  in spec 004/020), Security (Principle II plus Responsible AI controls below), Cost Optimization
  (model routing/caching to avoid always calling the most expensive model tier), Operational
  Excellence (structured telemetry per Principle IV's "one implementation" logging discipline), and
  Performance Efficiency (streaming-first responses, hybrid retrieval top-K tuning).
- **Responsible AI**: every model-facing boundary MUST apply Azure AI Content Safety (prompt
  shields, groundedness detection, content filtering) in addition to this spec set's existing PII
  redaction (see Security & Compliance Constraints below), and every deployed model/agent MUST have
  a documented evaluation pass (Foundry's built-in risk-and-safety evaluators or equivalent) before
  production use. This operationalizes Microsoft's six Responsible AI principles (Fairness,
  Reliability & Safety, Privacy & Security, Inclusiveness, Transparency, Accountability) — in
  particular Transparency (AI-generated content and agent identity must be disclosed to users,
  consistent with the existing PDF-export/Canvas-submission provenance concerns already in this
  spec set) and Accountability (every agent action must be traceable to an authenticated caller,
  per Principle II).

## Security & Compliance Constraints

- PII redaction is on by default for any user-authored text or third-party (Canvas) data reaching
  a model. If automated ML-based detection fails, the system MUST fall back to fail-closed
  regex/rule-based redaction — never fail open on a redaction failure.
- FERPA: no roster or student-list capability may be exposed through any tool or MCP surface, by
  design. This is a compliance boundary, not a feature gap to be closed later.
- Identity used for any external-system side effect (Canvas submission, A2A per-caller memory
  scoping) MUST be derived server-side from the verified session/context, never from
  client-supplied request data.
- LMS/Canvas OAuth tokens MUST be encrypted at rest with refresh and health checks, and LMS launch
  tokens MUST be validated for signature, required claims, and replay (JTI) protection before use
  (`docs/PRODUCT_REQUIREMENTS_DOCUMENT.md` REQ-LTI-3, REQ-NFR-SEC-4).
- Credentials and API keys (persona `apiKey`, data-product `apiKey`, Canvas OAuth tokens) MUST be
  excluded from any client-facing accessor structurally (at the type/accessor level), not by
  convention at each call site.

## Development Workflow

- Features are developed spec-first. `docs/PRODUCT_REQUIREMENTS_DOCUMENT.md` is the primary,
  forward-looking source of capability requirements (REQ-### statements) for the new platform;
  `docs/SSD_Document.md` is the as-is discovery source for the legacy accelerator, mined for
  hardening/correctness requirements per `docs/spec-kit-decomposition-plan.md`. Each
  independently-shippable feature gets its own `specs/NNN-feature-name/spec.md` following
  `.specify/templates/spec-template.md`, with prioritized (P1/P2/P3) user stories. Where a PRD
  capability area overlaps an existing SSD-derived spec, extend that spec rather than starting
  over, per `docs/prd-decomposition-plan.md`'s merge/new routing.
- A spec converting existing (buggy) behavior into a requirement MUST reframe the bug as the fixed
  target state in the FR text itself, keeping the as-is bug description only in that story's "Why
  this priority" — specs are forward-looking, never a transcript of current defects.
- `/speckit-plan` and `/speckit-tasks` follow each spec; the "Constitution Check" gate in
  `plan-template.md` must be reconciled against the principles above before implementation begins.

## Governance

This constitution supersedes ad hoc practice for anything it covers. Amendments require: (1) a
stated rationale, (2) a version bump per semantic versioning — MAJOR for incompatible principle
removal/redefinition, MINOR for a new principle or section, PATCH for wording/clarification only —
and (3) a Sync Impact Report prepended to this file describing the change. Any plan or spec found
to violate a principle above must either be brought into compliance or carry an explicit, justified
exception recorded in that plan's Complexity Tracking section.

**Version**: 1.2.0 | **Ratified**: 2026-07-20 | **Last Amended**: 2026-07-21
