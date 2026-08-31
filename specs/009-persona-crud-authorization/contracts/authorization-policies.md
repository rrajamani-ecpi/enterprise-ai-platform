# Contract: Authorization Policies

**Feature**: 009-persona-crud-authorization

## Composition of route-declarable policy + in-handler gate

Only `POST /api/personas/{id}/transfer-ownership` uses a route-declarable policy (`PolicyNames.RequireAdmin`, spec 002) — ownership transfer is an admin-only action with no resource-specific nuance. Every other route relies on the app-wide deny-by-default fallback policy (`RequireAuthenticated`, spec 002) plus an **in-handler** call to `PersonaAccessEvaluator`, because "is this caller allowed to touch *this specific* persona" depends on the resource (owner/collaborator/lesson-persona status), which a static ASP.NET Core policy cannot express — the same split already used by `ModelAccessEndpoints` (policy-based, admin-only) vs. `ChatEndpoints` (manual, ownership-based).

## The fixed non-revealing error contract (FR-004, research.md D4)

Every `PersonaAccessEvaluator`-gated rejection — whether the persona doesn't exist, or exists but the caller isn't the owner/a collaborator/admin/an eligible student — returns exactly:

```json
{ "status": "UNAUTHORIZED", "response": null, "errors": [{ "message": "You do not have access to this persona." }] }
```

mapped to HTTP `401`. This is a deliberate, single, hardcoded string — not a template that includes the persona ID, the caller's role, or any other varying detail — because any variation would be the enumeration signal FR-004/SC-002 forbids. `NotFound` (`404`) is never returned from `GET`/`PATCH`/`DELETE /api/personas/{id}` for this reason (D4) — only genuinely route-level 404s (e.g. an unmatched route pattern) use it, which is an ASP.NET Core framework behavior, not application code.

## Lesson-persona write block (FR-005)

`PersonaAccessEvaluator.CanWrite` returns `false` for `Edit`/`Delete` when `persona.IsLessonPersona && !caller.IsAdmin` — evaluated **after** confirming the caller has `FullAccess` or `ReadOnly` per `Evaluate`, so a lesson persona's designated collaborator (who has `FullAccess` under `Evaluate`) is still blocked from writing to it, per spec Edge Cases' explicit callout. Unlike the FR-004 case above, this caller already has (or had) legitimate access, so revealing the specific reason carries no enumeration risk — the rejection is:

```json
{ "status": "ERROR", "response": null, "errors": [{ "message": "This persona is a lesson persona and can only be modified by an admin." }] }
```

mapped to HTTP `400`.

## Group-token sharing block (FR-008)

A non-admin caller's write attempt that includes a `Group`-typed `SharedWith` entry is rejected — per the clarification resolving FR-008's "documented company-wide override" clause (no override exists in this build) — with:

```json
{ "status": "ERROR", "response": null, "errors": [{ "message": "Only admins may share a persona with a group token." }] }
```

mapped to HTTP `400`.

## Ownership-transfer conflict (FR-013, research.md D7)

`TransferOwnershipAsync` and every `PersonaAccessEvaluator.CanWrite`-gated write share the same `RowVersion` optimistic-concurrency column. A `DbUpdateConcurrencyException` from any of them maps to:

```json
{ "status": "ERROR", "response": null, "errors": [{ "message": "This persona was modified concurrently (an ownership transfer may be in progress) — reload and retry." }] }
```

mapped to HTTP `409 Conflict` — distinct from the `401` enumeration-prevention shape above, since a conflict is not an authorization signal and revealing it carries no enumeration risk (the caller already knows the persona exists, since they successfully loaded it before the conflicting write).
