# Decomposition Plan: SSD_Document.md → spec-kit Specs

Source: [SSD_Document.md](./SSD_Document.md) (11-domain discovery registry).
Goal: map its content onto spec-kit artifacts (`specs/NNN-feature/spec.md`, one per independently-shippable feature), reframing "as-is" EARS statements into "to-be" requirements.

Not every section of the source maps to a *feature spec* — some belongs one level up, in `/constitution` or `/plan` context instead. That routing is called out below.

## Routing by artifact type

| Source section | Target spec-kit artifact | Notes |
|---|---|---|
| §1 System architecture, §2 Data dictionary | `/constitution` + shared `/plan` context | Grounding facts every spec's `/plan` phase should inherit, not a feature in itself |
| §3.1–3.11 Domain deep-dives | One or more `spec.md` per domain | See table below — several domains split into 2+ specs where concerns are independently shippable |
| §4 Cross-domain integrations | `/plan` context (per affected spec) | Contract facts (e.g. "Logic Apps runs the real step-engine, not this repo") — informs `/plan`, not requirements themselves |
| §5 Architectural debt | Own `spec.md` per convergence target | Already problem/target-shaped; becomes its own cross-cutting hardening spec, not folded into the domain it touches |
| Domain J (Common Cross-Cutting Services) | `/constitution` (non-negotiable invariants) | Shared infra (DB abstraction, PII redaction, sharing engine) used by everything else — not itself a user-facing feature |

## Domain → spec mapping

| Domain | Proposed spec(s) | Why split (if split) |
|---|---|---|
| A. Auth & Identity | 1. Session & Role Derivation<br>2. Canvas LTI Launch & Impersonation Hardening | Impersonation downgrade logic is independently re-implemented 3x (a debt item) — worth isolating as its own hardening target |
| B. Chat Core | 1. Chat Message Pipeline & Model Access Control<br>2. Document Ingestion & Retrieval (RAG) | Ingestion/retrieval is independently testable/deployable from the live chat stream |
| C. Multi-Chat / Chat-Home / Lesson Mode | 1. Multi-Chat Session Persistence (fixes client-only state loss)<br>2. Lesson Mode & Canvas Submission | Different user journeys, different failure domains |
| D. Canvas LTI Integration & Student View | 1. Canvas Data-Access Tools & PII Redaction | Kept as one — cohesive around the Canvas OAuth + MCP tool surface |
| E. Persona Management & Persona Studio | 1. Persona CRUD & Authorization<br>2. AI-Assisted Persona Builder & Live Preview | Builder/preview is generative-AI-specific and independently valuable |
| F. Orchestration & Agents (A2A) | 1. Orchestration Builder: Persisted, Authorized Graph Editing (**worked example below**)<br>2. A2A Agent Invocation Contract | Builder correctness vs. external agent contract are separate audiences |
| G. Data Products | 1. Data Product Authorization Hardening (fixes unauthenticated URL edit/delete)<br>2. Ingestion Pipeline Reliability (retry/DLQ) | One is an urgent security fix, the other is ops reliability — different priority tiers |
| H. Admin / Settings / Executive Reporting | 1. Model & Access Config Management<br>2. Executive Reporting Reliability (fixes silent failure-masking) | Reporting-integrity fix is independently shippable from config-management features |
| I. Prompt Library | 1. Prompt CRUD, Sharing & Ownership Transfer | Kept as one; note `TransferPromptOwnerShip`'s trust-client-data bug as an FR inside it |
| K. Support Features | 1. Feedback, Changelog & Health Probes | Small enough to stay bundled; changelog directory's missing-guard bug becomes an FR |

## Reframing rule (applies to every spec above)

Source EARS statements describe **current** behavior, bugs included. When converting:
- A statement describing correct behavior → keep as an `FR-###` almost verbatim.
- A statement describing a bug or gap (e.g. "no-op auth," "no cycle detection," "client-only state") → invert it into the **fixed** requirement, and add the current bug as context in "Why this priority," not as the requirement text itself.
- A statement that's purely descriptive/architectural (e.g. "two competing implementations exist") → do not turn into an FR at all; it's `/plan`-level context about what to consolidate, not a testable requirement.

## Completed specs

All 17 specs proposed above have been drafted under `specs/NNN-feature-name/spec.md`, each following spec-kit's real template (fetched live from `github/spec-kit`) with prioritized P1–P3 user stories, FR-### requirements, and measurable SC-### success criteria:

| # | Spec | Domain |
|---|---|---|
| 001 | [Orchestration Builder: Persisted, Authorized Graph Editing](../specs/001-orchestration-builder/spec.md) | F |
| 002 | [Session Resolution & Role Derivation](../specs/002-session-role-derivation/spec.md) | A |
| 003 | [Canvas LTI Launch & Student-View Impersonation Hardening](../specs/003-canvas-lti-launch-impersonation/spec.md) | A |
| 004 | [Chat Message Pipeline, Tool Safety & Model Access Control](../specs/004-chat-message-pipeline/spec.md) | B |
| 005 | [Document Ingestion & Retrieval (RAG)](../specs/005-document-ingestion-retrieval/spec.md) | B |
| 006 | [Multi-Chat Session Persistence](../specs/006-multi-chat-session-persistence/spec.md) | C |
| 007 | [Lesson Mode & Canvas Assignment Submission](../specs/007-lesson-mode-canvas-submission/spec.md) | C |
| 008 | [Canvas Data-Access Tools & PII Redaction](../specs/008-canvas-data-access-pii-redaction/spec.md) | D |
| 009 | [Persona CRUD & Authorization](../specs/009-persona-crud-authorization/spec.md) | E |
| 010 | [AI-Assisted Persona Builder & Live Preview](../specs/010-persona-builder-live-preview/spec.md) | E |
| 011 | [A2A Agent Invocation Contract](../specs/011-a2a-agent-invocation-contract/spec.md) | F |
| 012 | [Data Product Authorization Hardening](../specs/012-data-product-authorization-hardening/spec.md) | G |
| 013 | [Data Product Ingestion Pipeline Reliability](../specs/013-ingestion-pipeline-reliability/spec.md) | G |
| 014 | [Model & Access Configuration Management](../specs/014-model-access-config-management/spec.md) | H |
| 015 | [Executive Reporting Reliability](../specs/015-executive-reporting-reliability/spec.md) | H |
| 016 | [Prompt CRUD, Sharing & Ownership Transfer](../specs/016-prompt-crud-sharing-ownership-transfer/spec.md) | I |
| 017 | [Feedback, Changelog & Health Probes](../specs/017-feedback-changelog-health-probes/spec.md) | K |

Domain J (Common Cross-Cutting Services) intentionally has no spec of its own — per the routing table above, it belongs in `/constitution`, not a feature spec.

Each spec was drafted independently (parallel agents, one per domain slice), so cross-spec consistency (numbering, no duplicated FRs between siblings like 001/011 or 002/003) was enforced via prompt instructions rather than a shared editing pass — worth a light consistency read-through before running `specify init` and importing these for real.
