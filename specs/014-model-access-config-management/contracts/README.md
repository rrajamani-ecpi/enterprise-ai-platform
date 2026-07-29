# Contracts: Model & Access Configuration Management

**Feature**: 014-model-access-config-management | **Date**: 2026-07-28

This feature exposes both **internal service contracts** (the effective-access computation, catalog, provider adapter) and a small **HTTP route surface** (admin mutation endpoints, open read endpoints, the preferences 401 fix) consumed by later features (chat/004, persona builder/010) and, in R2, an admin UI. R1 ships the service contracts and API routes; no admin UI consumes the write routes yet.

See also: [contracts/service-interfaces.md](./service-interfaces.md), [contracts/authorization-policies.md](./authorization-policies.md), [contracts/route-table.md](./route-table.md).
