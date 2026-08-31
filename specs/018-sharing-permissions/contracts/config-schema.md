# Contract: Configuration Schema

**Feature**: 018-sharing-permissions

Since `RoleSharingPolicy`/`GlobalSharingOverride` are static, ops-managed `appsettings`-bound config (no admin UI/API — spec Assumptions), this schema is the effective "API" administrators change to alter sharing behavior. Both sections are validated with `AddOptions<T>().ValidateDataAnnotations().ValidateOnStart()` — a malformed section fails application startup (research.md D7), not a request at runtime.

## `RoleSharing` section (binds `RoleSharingPolicyOptions`)

```json
{
  "RoleSharing": {
    "Roles": {
      "Employee": {
        "GroupSharingEnabled": true,
        "AllowedGroups": ["faculty", "students"]
      },
      "Contractor": {
        "GroupSharingEnabled": false,
        "AllowedGroups": []
      },
      "Student": {
        "GroupSharingEnabled": true,
        "AllowedGroups": ["students"]
      }
    }
  }
}
```

- **`Roles`** keys MUST be one of `Employee`, `Contractor`, `Student` — `Admin` MUST NOT appear (data-model.md validation invariant; admin's allow-everything behavior is hardcoded, never config-driven).
- `Employee` here is FR-004's "faculty role" — the PRD's "faculty" term maps onto the already-implemented `Employee` role flag (research.md D2); there is no separate `Faculty` key.
- Any non-admin `RoleName` may be **omitted** from `Roles` — an omitted role (e.g. `Contractor`, which spec.md defines no policy for) defaults to `{ GroupSharingEnabled: false, AllowedGroups: [] }` (data-model.md fail-safe default), not a startup failure. The example above configures `Contractor` explicitly only for illustration; omitting it entirely is equivalent.
- `AllowedGroups` entries are opaque deployment-defined tokens (`faculty`, `students`, `@employees`, `announcements`, etc. — spec Edge Cases); this schema does not constrain their values beyond non-null strings.

## `GlobalSharingOverride` section (binds `GlobalSharingOverrideOptions`)

```json
{
  "GlobalSharingOverride": {
    "DisableAllGroupSharing": false,
    "AdminOnlyMode": false,
    "GloballyAllowedGroups": ["announcements"]
  }
}
```

- Default/steady-state values are all-`false`/empty, matching "no override active" (spec Story 3's baseline).
- Toggling `DisableAllGroupSharing` or `AdminOnlyMode` to `true` is an ops/deployment action (config change + restart or config-reload, depending on hosting setup) — this feature defines the *effect* of these flags (data-model.md `SharingDecision` formula), not the activation mechanism itself (spec Assumptions).

## Startup validation failure behavior

Per Principle III (fail loud) applied to a security-relevant config surface: if either section is missing entirely, is malformed, or `RoleSharing:Roles` contains an `Admin` key, the application MUST fail to start with a clear validation error — it MUST NOT fall back to a permissive default for a structurally invalid config. An individual role being *absent* from `Roles` is not a validation failure: it resolves to the documented restrictive default above, not a silent fallback, since the default itself is recorded in this contract.
