# Contract: Service Interfaces

**Feature**: 009-persona-crud-authorization

Internal C# interfaces (namespaces abbreviated). Signatures are the contract; bodies belong to implementation/tasks. Reuses spec 002's `ServerActionResponse<T>`, `UserModel`, `ICurrentUserAccessor`, `IIdentityHasher`, `PolicyNames` rather than redefining equivalents (Principle IV).

## `IPersonaService` (FR-001–FR-013, all user stories)

```csharp
public interface IPersonaService
{
    // Enumeration-safe: returns Unauthorized (never NotFound) for both "doesn't exist" and
    // "exists but forbidden" (FR-004, research.md D4). Returns PersonaPublicDTO only.
    Task<ServerActionResponse<PersonaPublicDTO>> GetAsync(string personaId, UserModel caller);

    Task<ServerActionResponse<IReadOnlyList<PersonaPublicDTO>>> ListAsync(UserModel caller);

    // `draft` carries only caller-settable fields (Name/Description/PersonaMessage/Model/
    // Extensions/DataProducts/CollaboratorPartitionKeys/SharedWith) — mirrors IChatThreadStore's
    // flat-parameter convention rather than introducing a new cross-layer request DTO type.
    // Server-managed fields (Id, OwnerPartitionKey, ApiKey, RowVersion, CreatedAtUtc/UpdatedAtUtc,
    // IsLessonPersona) in `draft` are always ignored/overwritten, never trusted from the caller.
    Task<ServerActionResponse<PersonaPublicDTO>> CreateAsync(PersonaModel draft, UserModel caller);

    // Same `draft` contract as CreateAsync. Non-admin caller's isLessonPersona value is discarded
    // server-side (FR-006); DataProducts validated via PersonaExtensionRules before any write
    // (FR-011); rejected on a lesson-persona write attempt by a non-admin, even a collaborator (FR-005).
    Task<ServerActionResponse<PersonaPublicDTO>> UpdateAsync(string personaId, PersonaModel draft, UserModel caller);

    Task<ServerActionResponse<bool>> DeleteAsync(string personaId, UserModel caller);

    // Admin-only (route-declarable RequireAdmin). Atomic single-row update (research.md D1);
    // conflicts with a concurrent edit/delete surface as a specific error via RowVersion (D7).
    Task<ServerActionResponse<PersonaPublicDTO>> TransferOwnershipAsync(string personaId, string newOwnerEmail, UserModel caller);
}
```

- **Contract**: Every method returns `PersonaPublicDTO`, never `PersonaModel` — structurally impossible for `ApiKey` to reach a caller through this interface (FR-009), and compiler-checkable (a reflection test asserts `IPersonaService` has no member exposing `PersonaModel`/`ApiKey`). Exactly one implementation (architecture-tested).

## `IPersonaRawAccessor` (FR-010, US3 — internal, not Web-reachable by construction)

```csharp
// internal — visible only to EnterpriseAIPlatform.Infrastructure, the test projects, and a
// future spec-011 assembly via [assembly: InternalsVisibleTo(...)]. Web has no [InternalsVisibleTo]
// grant, so it cannot reference this interface at all — a compiler error, not a convention.
internal interface IPersonaRawAccessor
{
    Task<ServerActionResponse<PersonaModel>> GetRawAsync(string personaId, CancellationToken cancellationToken = default);
}
```

- **Contract**: The one legitimate path to a full `PersonaModel` (including `ApiKey`), reserved for spec 011's future A2A credential comparison. Implemented by the same `PersonaService` class as `IPersonaService`, but on a separate `internal` interface — deliberately stronger than a source-scan/grep-based check (research.md/analyze T1 finding), since `internal` visibility is enforced by the compiler, not a text pattern that could miss a dynamic or reflection-based call site.

## `PersonaAccessEvaluator` (Application-layer pure function; not DI-registered)

```csharp
public static class PersonaAccessEvaluator
{
    public static PersonaAccessResult Evaluate(PersonaModel persona, UserModel caller);

    public static bool CanWrite(PersonaModel persona, UserModel caller, PersonaOperation operation);
}

public enum PersonaAccessResult { FullAccess, ReadOnly, Denied }
public enum PersonaOperation { Read, Edit, Delete }
```

- **Contract**: Pure, DI-free — the unit-test surface for the full role × ownership × lesson-persona matrix (SC-002/SC-003, research.md D3). `IPersonaService`'s implementation calls this before every read/write and maps `Denied`/blocked-write to the fixed `Unauthorized` response (D4) — never `NotFound`.

## `PersonaExtensionRules` (Application-layer pure function; not DI-registered)

```csharp
public static class PersonaExtensionRules
{
    public static bool TryValidate(IReadOnlyList<string> extensions, IReadOnlyList<string> dataProducts, out string? error);
}
```

- **Contract**: Called from every create/update entry point before persistence (FR-011, D6) — mirrors `ConversationRenameRules`/`MultiChatQuadrantRules`'s existing shape. Treats an empty-string-array `dataProducts` the same as an absent one (spec Edge Cases).

## Reused from spec 002 (not redefined here)

- `ServerActionResponse<T>` / `ResponseStatus` / `ActionError` — the app-wide result envelope.
- `UserModel` / `RoleFlags` — supplies `IsAdmin`/`IsStudent` and the caller's email (hashed via `IIdentityHasher.ForEmail` for comparison against `PersonaModel.OwnerPartitionKey`/`CollaboratorPartitionKeys`).
- `ICurrentUserAccessor` — resolves the caller server-side; never trusts a client-supplied identity (Principle II).
- `PolicyNames.RequireAdmin` — the route-declarable admin gate for `TransferOwnershipAsync`'s endpoint.
- `IIdentityHasher.ForEmail` — the sole identity-hashing utility (research.md D2).
