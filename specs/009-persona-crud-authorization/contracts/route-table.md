# Contract: Route Table

**Feature**: 009-persona-crud-authorization

Minimal-API routes (`PersonaEndpoints.MapPersonaEndpoints`), matching `ChatEndpoints`/`ModelAccessEndpoints`'s `/api/{feature}/{resource}` convention. No policy is route-declarable except the admin-only transfer route — ownership/collaborator/lesson-persona gating is resource-specific and is checked in-handler via `PersonaAccessEvaluator` (research.md D10).

| Route | Purpose | Requirement |
|---|---|---|
| `GET /api/personas` | List personas visible to the caller (owner, collaborator, admin-all, or student-visible lesson personas) | Authenticated (`RequireAuthenticated` — deny-by-default fallback policy); non-admin listings exclude lesson personas server-side (FR-007) |
| `GET /api/personas/{id}` | Read a persona | Authenticated; `PersonaAccessEvaluator` in-handler (FR-003/FR-004) |
| `POST /api/personas` | Create a persona | Authenticated; `PersonaExtensionRules.TryValidate` before persistence (FR-011) |
| `PATCH /api/personas/{id}` | Update a persona | Authenticated; `PersonaAccessEvaluator.CanWrite` in-handler; lesson-persona write blocked for non-admin (FR-005); submitted `isLessonPersona` discarded for non-admin (FR-006); `PersonaExtensionRules` re-validated (FR-011); `RowVersion` conflict → specific error (FR-013) |
| `DELETE /api/personas/{id}` | Delete a persona | Authenticated; `PersonaAccessEvaluator.CanWrite` in-handler; lesson-persona delete blocked for non-admin (FR-005) |
| `POST /api/personas/{id}/transfer-ownership` | Transfer ownership to a new owner | `RequireAdmin` (route-declarable, mirrors `ModelAccessEndpoints`'s admin writes) — FR-001/FR-002/FR-013 |

**Enumeration-prevention note** (FR-004/D4): every in-handler `PersonaAccessEvaluator` rejection on `GET`/`PATCH`/`DELETE` maps to the same fixed `ServerActionResponse.Unauthorized("You do not have access to this persona.")` → `Results.Json(..., statusCode: 401)`, regardless of whether the persona exists — never `Results.NotFound()` from these three routes.
