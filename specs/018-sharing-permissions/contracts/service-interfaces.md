# Contract: Service Interfaces

**Feature**: 018-sharing-permissions

Internal C# interfaces (namespaces abbreviated). Signatures are the contract; bodies belong to implementation/tasks. Reuses spec 002's `UserModel`/`RoleFlags` rather than redefining caller-identity shapes (Principle IV).

## `ISharingPolicyService` (FR-002, FR-006–010, FR-015, US2/US3)

```csharp
public interface ISharingPolicyService
{
    // Computed fresh on every call from the caller's current UserModel and the current
    // IOptionsSnapshot<RoleSharingPolicyOptions>/IOptionsSnapshot<GlobalSharingOverrideOptions>
    // values — never cached per-user, never trusts a client-supplied validity signal (FR-015).
    SharingDecision Evaluate(UserModel caller, ShareTargetRequest request);
}
```

- **Contract**: `Evaluate` is synchronous and side-effect-free — no I/O, no persistence (D1). It MUST produce identical results whether invoked from a UI-originated call or a direct API call (FR-006), which follows structurally from there being exactly one implementation and no client-supplied input to the decision other than `caller` (server-derived) and `request` (the target being attempted). Exactly one implementation (architecture test, mirroring spec 014's `ModelAccessSingleImplementationTests` pattern).

## `SharingPolicyEvaluator` (Application-layer pure function; not DI-registered)

```csharp
public static class SharingPolicyEvaluator
{
    public static SharingDecision Evaluate(
        RoleFlags callerRoles,
        ShareTargetRequest request,
        RoleSharingPolicyOptions rolePolicy,
        GlobalSharingOverrideOptions globalOverride);
}
```

- **Contract**: Pure function per data-model.md's `SharingDecision` formula (research.md D5). `ISharingPolicyService`'s sole implementation (`SharingPolicyService`, Infrastructure) resolves `caller.Roles` and both current `IOptionsSnapshot<T>` values and delegates here — this static method is the unit-test surface for the full role × target-type × override combinatorial matrix (SC-002 through SC-005), requiring no mocking.

## Reused from spec 002 (not redefined here)

- `UserModel` / `RoleFlags` — supplies the caller's role flags (`IsAdmin`, `IsEmployee`, `IsContractor`, `IsStudent`) consumed by `Evaluate`.
- `RoleName` — the enum `RoleSharingPolicyOptions.Roles` is keyed by (research.md D2).

## Not provided by this feature

- No mutation API for `RoleSharingPolicyOptions`/`GlobalSharingOverrideOptions` — both are ops-managed static config (spec Assumptions; see [config-schema.md](./config-schema.md)).
- No integration contract for 009/012/016 calling `ISharingPolicyService` — those specs' own refactor onto this contract is out of scope for 018.
