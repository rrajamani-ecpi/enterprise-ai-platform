<!--
Sync Impact Report
Version change: 1.0.0 → 1.1.0 (MINOR — broadened principle scope + new primary source added)
Modified principles:
  - II. "Explicit Authorization Before Every Mutation" → "Explicit, Server-Side Authorization for Every Access"
    (broadened from mutation-only to all access — reads, tool/model availability, entitlements)
Added sections: none (existing sections extended in place)
Removed sections: none
Templates requiring updates:
  - .specify/templates/plan-template.md ✅ no changes needed — its "Constitution Check" gate reads this file dynamically
  - .specify/templates/spec-template.md ✅ no changes needed — unaffected by this amendment
  - .specify/templates/tasks-template.md ✅ no changes needed — unaffected by this amendment
Follow-up TODOs: none
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

**Version**: 1.1.0 | **Ratified**: 2026-07-20 | **Last Amended**: 2026-07-21
