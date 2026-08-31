# Contracts: Prompt CRUD, Sharing & Ownership Transfer

- [service-interfaces.md](./service-interfaces.md) — `IPromptService`, `IPromptGenerationService`, `PromptAccessEvaluator`, `PromptValidationRules`, and the `ChatComposerState` seeding contract.
- [route-table.md](./route-table.md) — the HTTP CRUD, ownership-transfer, favorites, and generator routes with their authorization requirements.
- [authorization-policies.md](./authorization-policies.md) — how `PromptAccessEvaluator`'s in-handler gate composes with route-declarable policies, the fixed non-revealing error contract (FR-009), and the structural field-injection defence (FR-005).
