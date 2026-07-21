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

Status legend: **merge** = extend an existing `specs/00X` spec with PRD's REQ-### as base FRs (don't restart); **new** = no existing spec covers this, draft fresh from REQ-### statements. **✅ done** = merge/creation completed.

| PRD § | Capability | Priority | Status | Notes |
|---|---|---|---|---|
| 4.1 | Auth, Sessions & Access Control | P1 | ✅ done — merged → 002, 003 | Added Story 5/6 to 002 (route/admin gating, identity hashing) and extended 003 (LMS identity hashing, impersonation audit) |
| 4.2 | Conversational Chat (core) | P1 | ✅ done — merged → 004 | Added Stories 6/7/8 (context compression, max-thread-size, feedback capture); REQ-CHAT-7 fully deferred to spec 005 |
| 4.3 | AI Models & Provider Registry | P1 | ✅ done — merged → 014 | Added Stories 6/7/8 (catalog metadata, provider adaptation, workload-identity auth) |
| 4.10 | Sharing & Permissions | P1 | ✅ done — **new spec 018** | `specs/018-sharing-permissions/spec.md`; found and documented a real gap (data products have no student-sharing branch) as an Edge Case for 012 to eventually close |
| 4.5 | RAG & Knowledge Grounding | P1 (pillar) | ✅ done — merged → 005 | Added Stories 5/6/7 (dual retrieval-scope, citations, deletion) + extended chunk/embedding FRs with explicit config |
| 4.6 | Data Products | P1 (pillar) | ✅ done — merged → 012, 013 + **new spec 019** | `specs/019-data-products-core/spec.md` owns base CRUD/versioning/MCP/rate-limiting/audit; 012/013 updated to cross-reference it instead of re-specifying |
| 4.7 | Personas | P1 (pillar) | ✅ done — merged → 009, 010 | REQ-PERSONA-7 cross-linked to spec 011; **REQ-PERSONA-2 (start chat from persona) has no home in either spec — flagged as an open gap, see note below** |
| 4.8 | Prompts | P1 (pillar) | ✅ done — merged → 016 | Added Stories 4/5/6 (base CRUD, favoriting, launch-from-prompt) — 016 previously only had the transfer/sharing bug fixes |
| 4.11 | Canvas LMS/LTI & Lessons | P1 (pillar) | ✅ done — merged → 003, 007, 008 | Closed 3 small gaps: REQ-LTI-7 error-code routing (003), explicit lesson-exit affordance (007), OAuth token health-check (008) |
| 4.12 | Orchestration | P1 (pillar) | ✅ done — merged → 001 | Added Story 5 (typed triggers: API/file-upload/multi-modal + config); REQ-ORCH-4 (execution/streaming) explicitly scoped out as an Assumption — that's the external Logic Apps engine, not in this repo |
| 4.13 | Agents & Interop (A2A/MCP) | P1 (pillar) | ✅ done — merged → 011 | Added Stories 5/6 (consuming external MCP servers, admin MCP monitoring dashboard); REQ-AGENT-3 cross-referenced to spec 019 |
| 4.4 | Tools/Extensions framework | P2 | ✅ done — **new spec 020** | `specs/020-tools-extensions-framework/spec.md`; owns the 4-layer gating model + catalog list, cross-references 004/007/008/011/012/019/014 for per-tool mechanics rather than duplicating |
| 4.18 | Admin Configuration | P2 | ✅ done — merged → 014, 015 | REQ-ADMIN-3 (MCP monitoring) cross-referenced to spec 011's Story 6; REQ-ADMIN-2 (cache fallback) was the one genuine gap, closed with a new FR in 014 |
| 4.9 | Multi-Chat (model comparison) | P2 | ✅ done — merged → 006 | Added Story 4 (P1) for the base parallel-dispatch/side-by-side capability, cross-referencing 014's model allow-list |
| 4.16 | Artifacts | P2 | ✅ done — merged → 004 | Added Story 9 for the base artifact-panel capability, deferring to the existing sandboxing story rather than restating it |
| 4.22 | File Upload & Processing | P2 | ✅ done — merged → 005 | Closed REQ-FILE-1/2 gaps (upload validation, actionable corrupt-file errors); REQ-FILE-3 (unified deletion) was already fully covered |
| 4.17 | PII Redaction | P2 | kept distributed (no action) | Already folded into 004 (chat) and 008 (Canvas three-tier); no third surface has emerged that would justify consolidating |
| 4.21 | Changelog & Notifications | P3 | ✅ done — merged → 017 | Added Story 5 for REQ-NOTIF-3 (WebSocket real-time notifications), with an explicit Assumption that the transport must be Azure-native, not the AWS endpoint SSD_Document.md flagged as debt |
| 4.20 | Analytics / Executive Dashboard | P3 | ✅ done — merged → 015 | Confirmed already fully covered; cross-reference only, no new content |
| 4.19 | User Preferences | P3 | ✅ done — **new spec 021** | `specs/021-user-preferences/spec.md`; 2 stories, self-healing landing-action fallback specified concretely |
| 4.14 | Image Generation & Vision | P3 | ✅ done — **new spec 022** | `specs/022-image-generation-vision/spec.md`; cross-references 004's existing size-limit FR and 014's vision-capability flag rather than duplicating |
| 4.15 | Voice Chat | P3 | ✅ done — **new spec 023** | `specs/023-voice-chat/spec.md`; 3 stories, one (connection-drop renegotiation) explicitly flagged as inferred beyond the PRD's single REQ-VOICE-1 statement |

## Constitution note

✅ Done — the constitution was amended to v1.1.0: it now cites `docs/PRODUCT_REQUIREMENTS_DOCUMENT.md` as primary source alongside SSD_Document.md, Principle II was broadened from mutation-only to every server-side access decision (REQ-NFR-SEC-1), and Principle III now cites REQ-NFR-REL-1 as its fail-open carve-out example.

## Closed gap: REQ-PERSONA-2

✅ Done — the persona merge pass found a PRD requirement with no natural home in either persona spec: **REQ-PERSONA-2** ("start a chat directly from a persona, applying its model, instructions, tools, and data products"). Resolved with a follow-up merge into spec 004 as User Story 10 (FR-036–FR-039, SC-015/016), cross-referencing spec 009 for the `PersonaModel` schema rather than redefining it.

## Suggested order of attack

1. ✅ **Done** — Foundational P1s: **Auth & Access Control** (merged → 002/003), **Sharing & Permissions** (new spec 018), **AI Model Registry** (merged → 014).
2. ✅ **Done** — the five PRD pillars (§1.1): Chat Core → RAG → Personas/Prompts → LMS/Lessons → Orchestration/Agents.
3. ✅ **Done** — P2 gap-fills (Tools/Extensions framework, Admin MCP monitoring, Multi-Chat base capability, Artifacts, unified file deletion).
4. ✅ **Done** — P3 net-new capabilities (Image Gen, Voice, User Preferences) and remaining polish (Analytics, Notifications).
5. ✅ **Done** — Closed the one open gap found along the way: REQ-PERSONA-2, merged into spec 004 (see above).

**All PRD capability areas are now represented across 23 specs**, with a clean consistency pass behind them (verified: no FR/SC numbering gaps or duplicates in any of the 23 files, no leftover template placeholders, all required template sections present, and a sampled set of cross-spec FR citations all resolve to real, matching content). This is a solid base to move into `/speckit-plan` for whichever spec you want to implement first.

**All 20 active rows complete as of 2026-07-21** (every row except 4.17, which is intentionally kept distributed with no action needed). Four new specs created beyond the original 17: **018** (Sharing & Permissions), **019** (Data Products Core), **020** (Tools/Extensions Framework), **021** (User Preferences), **022** (Image Generation & Vision), **023** (Voice Chat) — six new specs total, bringing the repo to 23 specs. One open follow-up remains: **REQ-PERSONA-2** (see above) needs its own small merge into spec 004, cross-referencing spec 009.
