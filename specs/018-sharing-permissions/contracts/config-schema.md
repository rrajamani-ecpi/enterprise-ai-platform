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

Per Principle III (fail loud) applied to a security-relevant config surface: if either section is missing, malformed, or contains an `Admin` key under `RoleSharing:Roles`, the application MUST fail to start with a clear validation error — it MUST NOT fall back to a permissive or restrictive default silently.
