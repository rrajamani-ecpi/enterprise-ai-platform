# Contracts: Persona CRUD & Authorization

- [service-interfaces.md](./service-interfaces.md) — `IPersonaService`, `PersonaAccessEvaluator`, `PersonaExtensionRules` internal contracts.
- [route-table.md](./route-table.md) — the HTTP CRUD + ownership-transfer routes and their authorization requirement.
- [authorization-policies.md](./authorization-policies.md) — how `PersonaAccessEvaluator`'s in-handler gate composes with the route-declarable `RequireAdmin` policy, and the fixed non-revealing error contract (FR-004).
