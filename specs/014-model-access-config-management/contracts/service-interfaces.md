# Contract: Service Interfaces

**Feature**: 014-model-access-config-management

Internal C# interfaces (namespaces abbreviated). Signatures are the contract; bodies belong to implementation/tasks. All reuse spec 002's `ServerActionResponse<T>`, `UserModel`, and `PolicyNames` rather than redefining equivalents (Principle IV).

## `IModelAccessService` (FR-003, US2)

```csharp
public interface IModelAccessService
{
    // Computed fresh on every call from current UserModel + current ModelConfigDocument state — never cached per-user.
    Task<ServerActionResponse<IReadOnlyList<ModelConfigDocument>>> GetAvailableModelsAsync(UserModel caller);
}
```

- **Contract**: `GetAvailableModelsAsync` applies `ComputeEffectiveAccess` (data-model.md `EffectiveModelAccess`) to every enabled, non-deleted model in the registry. Exactly one implementation (architecture test, mirroring spec 002's SC-002 pattern).

## `IModelCatalogService` (FR-002, FR-011, US2/US6)

```csharp
public interface IModelCatalogService
{
    Task<ServerActionResponse<ModelConfigDocument>> GetAsync(string canonicalId, bool includeDeleted = false);
    Task<ServerActionResponse<IReadOnlyList<ModelConfigDocument>>> ListAsync(bool includeDeleted = false);
    Task<ServerActionResponse<Unit>> UpsertAsync(ModelConfigDocument model); // RequireAdmin at the endpoint layer
    Task<ServerActionResponse<Unit>> SoftDeleteAsync(string canonicalId);    // sets IsDeleted=true only
    Task<ServerActionResponse<string>> ResolveAliasAsync(string canonicalId); // follows ModelAliasDocument if retired
}
```

- **Contract**: `SoftDeleteAsync` MUST NOT physically remove the row (FR-002/SC-003). `UpsertAsync` rejects a model with any capability flag unset (Edge Case, data-model.md validation invariant).

## `IMessageLimitConfigService` / `IPersonaGenerationModelConfigService` (FR-004–009, US3/US4)

```csharp
public interface IMessageLimitConfigService
{
    Task<ServerActionResponse<MessageLimitConfig>> GetAsync();       // open read, FR-004
    Task<ServerActionResponse<Unit>> SetAsync(int? perMessageCap, int? dailyCap); // RequireAdmin, FR-006/008
}

public interface IPersonaGenerationModelConfigService
{
    Task<ServerActionResponse<PersonaGenerationModelConfig>> GetAsync();  // open read, FR-005
    Task<ServerActionResponse<Unit>> SetAllowedModelsAsync(IReadOnlyList<string> modelIds); // RequireAdmin, FR-007
    Task<ServerActionResponse<Unit>> ValidateSelectionAsync(string modelId); // FR-009, used by persona-gen callers
}
```

- **Contract**: `SetAsync` rejects any non-null cap that is not an integer ≥1, regardless of caller (FR-008) — enforced here, not only at a DTO layer. `ValidateSelectionAsync` fails closed (actionable error) if the allow-list is empty/misconfigured (Edge Case) rather than falling back to the general registry.

## `IModelProviderAdapter` (FR-012/013, US7/US8 — R1: one implementation)

```csharp
public interface IModelProviderAdapter
{
    string Provider { get; } // e.g. "azure-foundry"
    Task<ProviderRequest> AdaptRequestAsync(ChatRequest request, ModelConfigDocument model);
    Task<ChatResponse> AdaptResponseAsync(ProviderResponse response, ModelConfigDocument model);
}
```

- **Contract**: One implementation per provider (architecture test). R1 registers exactly `AzureFoundryProviderAdapter`, which authenticates via `DefaultAzureCredential`/workload identity (FR-013) — no static API key anywhere in `AdaptRequestAsync` or its configuration. `AdaptResponseAsync` MUST normalize provider-specific error shapes into one consistent error shape (Edge Cases) rather than leaking a provider-specific shape to the caller. R2 adds `ClaudeProviderAdapter`, `VertexProviderAdapter`, etc. against this same interface — no change to `IModelAccessService` or any R1 caller required.

## Reused from spec 002 (not redefined here)

- `ServerActionResponse<T>` / `ResponseStatus` / `ActionError` — the app-wide result envelope.
- `UserModel` — supplies `IsAdmin`, `AdvancedModelAccess`, and role flags consumed by `IModelAccessService`.
- `PolicyNames.RequireAdmin` — the admin gate applied at the endpoint layer for every write route in route-table.md.
