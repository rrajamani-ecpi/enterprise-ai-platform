# Contracts: Sharing & Permissions Policy

This feature exposes exactly two contracts — no HTTP routes (see research.md D8):

- [service-interfaces.md](./service-interfaces.md) — the internal `ISharingPolicyService` contract that future consumers (specs 009/012/016, out of scope here) will call instead of re-implementing share-target validity.
- [config-schema.md](./config-schema.md) — the `appsettings.json` shape ops/deployment must populate for `RoleSharingPolicyOptions` and `GlobalSharingOverrideOptions`, since both are static-config-only (no admin UI/API).

There is no `route-table.md`: this feature adds no Web-layer endpoint.
