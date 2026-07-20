# Decomposition Plan: PRODUCT_REQUIREMENTS_DOCUMENT.md → spec-kit Specs

Source: [PRODUCT_REQUIREMENTS_DOCUMENT.md](./PRODUCT_REQUIREMENTS_DOCUMENT.md) — a vendor-neutral, forward-looking capability spec (REQ-### "SHALL" statements across 22 sections), explicitly written to let a team build an equivalent system on a technology stack of their choosing.

This is a **different north star** from [SSD_Document.md](./SSD_Document.md): SSD is as-is discovery of the existing accelerator's bugs, which [specs 001–017](./spec-kit-decomposition-plan.md) already reframed into hardening fixes. This plan treats the PRD as the primary source for a *new* platform build, and folds the existing 17 specs in as a secondary "lessons learned" layer wherever a capability area overlaps.

## Routing by artifact type

| PRD section | Target artifact | Notes |
|---|---|---|
| §2 User Roles & Personas, §3 Architecture (reference), §5 Data Model, §6 External Integrations | `/constitution` + shared `/plan` context | Grounding facts, not features in themselves — same treatment as SSD §1/§2/§4 |
| §4.1–§4.22 Functional Requirements | One or more `spec.md` per capability area | See table below |
| §7 Non-Functional Requirements | Folded into every spec's FRs/Success Criteria, not a standalone spec | Org-wide invariants (server-side-only auth, no secrets to client, telemetry everywhere) belong in the constitution instead — see Constitution note below |
| §9 Out of Scope | Explicit exclusions to copy into each relevant spec's Assumptions | Prevents scope creep back into fine-tuning, native mobile, billing, etc. |

## Capability area → spec mapping

Status legend: **merge** = extend an existing `specs/00X` spec with PRD's REQ-### as base FRs (don't restart); **new** = no existing spec covers this, draft fresh from REQ-### statements.

| PRD § | Capability | Priority | Status | Notes |
|---|---|---|---|---|
| 4.1 | Auth, Sessions & Access Control | P1 | merge → 002, 003 | REQ-AUTH-1..5 mostly already covered; REQ-ROLE-3 (LMS-launch identity without email) cross-check against 003 |
| 4.2 | Conversational Chat (core) | P1 | merge → 004 | Adds context-window compression (REQ-CHAT-5) and max-thread-size handling (REQ-CHAT-10) not yet in 004 |
| 4.3 | AI Models & Provider Registry | P1 | merge → 014 | REQ-MODEL-4 (per-provider request/response adaptation) is new beyond 014's admin-gate focus |
| 4.10 | Sharing & Permissions | P1 | **new**, consolidate | Currently scattered across 009/012/016; PRD treats it as one cross-cutting policy (global overrides, per-role targets) — worth centralizing into one spec the others reference |
| 4.5 | RAG & Knowledge Grounding | P1 (pillar) | merge → 005 | Adds explicit chunk-overlap config and dual retrieval-scope (per-chat vs. data-product) as first-class FRs |
| 4.6 | Data Products | P1 (pillar) | merge → 012, 013 + **new** base CRUD spec | 012/013 are hardening-only (auth gap, DLQ); PRD's full CRUD/versioning/MCP-exposure/bulk-ingestion capability isn't fully specified yet |
| 4.7 | Personas | P1 (pillar) | merge → 009, 010 | Good coverage already; add REQ-PERSONA-7 (A2A publish) cross-link to spec 011 |
| 4.8 | Prompts | P1 (pillar) | merge → 016 | Good coverage already |
| 4.11 | Canvas LMS/LTI & Lessons | P1 (pillar) | merge → 003, 007, 008 | Split across three specs already; PRD's REQ-LTI-6 (multi-environment, no identity collision) confirm covered in 003 |
| 4.12 | Orchestration | P1 (pillar) | merge → 001 | 001 only covers builder persistence/auth/cycle bugs; PRD's trigger types (API/file-upload/multi-modal, each with auth/rate-limit/file-constraint config) are a gap to fill |
| 4.13 | Agents & Interop (A2A/MCP) | P1 (pillar) | merge → 011 | 011 covers A2A invocation only; PRD adds *consuming* external MCP servers and an admin MCP monitoring dashboard — both gaps |
| 4.4 | Tools/Extensions framework | P2 | **new**, cross-ref 004 | The layered gating model (requiresAdmin/isDemo/requiresAdvancedModel/allowedModels) and the tool catalog itself deserve their own spec rather than living inside chat-pipeline |
| 4.18 | Admin Configuration | P2 | merge → 014, 015 + gap | MCP monitoring dashboard (REQ-ADMIN-3) not covered by either |
| 4.9 | Multi-Chat (model comparison) | P2 | merge → 006 | 006 only fixed the persistence bug; the base "compare N models side-by-side" capability (REQ-MULTICHAT-1) needs its own FRs |
| 4.16 | Artifacts | P2 | merge → 004 (extend) | 004 only covers the sandboxing bug; full artifact-panel capability (REQ-ARTIFACT-1) is broader |
| 4.22 | File Upload & Processing | P2 | merge → 005 | Unified deletion across chat/data-product scopes (REQ-FILE-3) is a gap |
| 4.17 | PII Redaction | P2 | keep distributed | Already folded into 004 (chat) and 008 (Canvas three-tier); consolidate into its own policy spec only if a third surface needs it |
| 4.21 | Changelog & Notifications | P3 | merge → 017 + gap | 017 covers changelog crash-guard; real-time WebSocket notifications (REQ-NOTIF-3) is new |
| 4.20 | Analytics / Executive Dashboard | P3 | merge → 015 | Good coverage already |
| 4.19 | User Preferences | P3 | **new** | Small: theme + landing-action resolution with self-healing fallback |
| 4.14 | Image Generation & Vision | P3 | **new** | No existing spec |
| 4.15 | Voice Chat | P3 | **new** | No existing spec; realtime session/token negotiation is novel enough to need its own FRs |

## Constitution note

The ratified constitution (`.specify/memory/constitution.md` v1.0.0) draws its principles from SSD_Document.md §5's debt findings. Its principles still hold for a PRD-driven build (authorization-before-mutation, fail-loud, one-implementation-per-concern, etc.), but it currently cites only SSD_Document.md as source material. A minor amendment (PATCH or MINOR bump) should add PRODUCT_REQUIREMENTS_DOCUMENT.md as the primary spec source and fold in the §7 NFRs that aren't yet principles in their own right — most notably REQ-NFR-SEC-1/2 (server-side-only auth, no secrets to client) and REQ-NFR-REL-1 (optional dependencies fail open without breaking core chat), which are stated once in the PRD but should apply project-wide, not per-spec.

## Suggested order of attack

1. Foundational P1s first, since every other spec depends on them: **Auth & Access Control** (merge 002/003), **Sharing & Permissions** (new, consolidate), **AI Model Registry** (merge 014).
2. The five PRD pillars (§1.1) next, in the order a user would encounter them: Chat Core → RAG → Personas/Prompts → LMS/Lessons → Orchestration/Agents.
3. P2 gap-fills (Tools/Extensions framework, Admin MCP monitoring, Multi-Chat base capability, Artifacts, unified file deletion).
4. P3 net-new capabilities (Image Gen, Voice, User Preferences) and remaining polish (Analytics, Notifications).

No specs have been drafted yet against this plan — this is the mapping only, for review before committing to writing any of the new/merged spec files.
