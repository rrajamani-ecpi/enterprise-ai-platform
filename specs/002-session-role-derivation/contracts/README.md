# Contracts: Session Resolution & Role Derivation

**Feature**: 002-session-role-derivation | **Date**: 2026-07-21

This feature exposes **internal service contracts, an authorization-policy catalog, and a route table** rather than a public HTTP API (it is the platform's server-side auth spine, consumed in-process by Blazor components and endpoints). These contracts are the stable surface later features depend on.

See also: [contracts/service-interfaces.md](./service-interfaces.md), [contracts/authorization-policies.md](./authorization-policies.md), [contracts/route-table.md](./route-table.md).
