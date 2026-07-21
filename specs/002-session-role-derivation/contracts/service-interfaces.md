# Contract: Service Interfaces

**Feature**: 002-session-role-derivation

Internal C# interfaces (namespaces abbreviated). These are the canonical, single-implementation services every later feature consumes. Signatures are the contract; bodies belong to implementation/tasks.

## `ICurrentUserAccessor` (FR-001, FR-005/006)

```csharp
public interface ICurrentUserAccessor
{
    // Never throws for "no session"; returns UNAUTHORIZED instead.
    ServerActionResponse<UserModel> GetCurrentUser();
}
```

- **Contract**: Reads role flags from the already-transformed `ClaimsPrincipal` (D2/D3). MUST NOT re-derive roles. Returns `UNAUTHORIZED` when no active session (FR-005). Exactly one implementation (SC-002).

## `IRoleResolver` (FR-004)

```csharp
public interface IRoleResolver
{
    RoleFlags DeriveFrom(IReadOnlyCollection<Guid> entraGroupIds);
}
```

- **Contract**: Single group-GUID → flag mapping (D2/D11). Independent, non-exclusive flags. Backed by validated Options config.

## `RoleDowngrade` (FR-002/003/009) — single pure function

```csharp
public static RoleFlags Apply(RoleFlags flags, bool impersonateAsStudent);
// impersonateAsStudent == true  => { IsAdmin=false, IsEmployee=false, IsContractor=false, IsStudent=true }
```

- **Contract**: Idempotent; the ONLY place the downgrade exists (SC-002, Principle IV). Callers (claims transformation, current-user accessor) invoke this rather than re-implementing. Fails closed if inputs cannot be evaluated (FR-009).

## `IIdentityHasher` (FR-014)

```csharp
public interface IIdentityHasher
{
    StoragePartitionKey ForEmail(string email); // SHA-256(lowercase+trim(email))
}
```

- **Contract**: Deterministic across casing/whitespace (SC-008). Never returns the raw email. LMS-student identity hashing is spec-003's concern.

## `ServerActionResponse<T>` (FR-005/006)

```csharp
public enum ResponseStatus { OK, ERROR, NOT_FOUND, UNAUTHORIZED }

public sealed record ServerActionResponse<T>(
    ResponseStatus Status,
    T? Response = default,
    IReadOnlyList<ActionError>? Errors = null);

public sealed record ActionError(string Message);
```

- **Contract**: The app-wide envelope reused verbatim (spec Assumptions). Success carries `Response`; failure carries `Errors`.
