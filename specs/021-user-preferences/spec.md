# Feature Specification: User Preferences

**Feature Branch**: `021-user-preferences`

**Created**: 2026-07-21

**Status**: Draft

**Input**: Derived from `docs/PRODUCT_REQUIREMENTS_DOCUMENT.md` §4.19 (User Preferences) — REQ-PREF-1 (persist per-user preferences including theme and default landing action) and REQ-PREF-2 (resolve the landing action — new chat / favorite persona / favorite prompt — and gracefully fall back when the referenced resource no longer exists). Per `docs/prd-decomposition-plan.md`, this is a small, previously uncovered capability. Landing-action resolution references, but does not redefine, the favorite-persona and favorite-prompt entities owned by `specs/009-persona-crud-authorization/spec.md` and `specs/016-prompt-crud-sharing-ownership-transfer/spec.md` respectively; the latter's FR-018 explicitly defers landing-action configuration and fallback behavior to this spec.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Persist theme and default landing action (Priority: P1)

A user sets a light/dark theme and chooses what they want to see when they open the app: a new chat, a specific favorite persona (opens a direct chat with that persona), or a specific favorite prompt (opens a chat pre-seeded with that prompt). These choices are remembered across sessions and devices.

**Why this priority**: Without persistence, every other part of this spec has nothing to act on. This is the baseline capability the rest of the feature builds on.

**Independent Test**: Set a theme and a landing-action preference (each of the three kinds), reload the app in a new session, and confirm both the theme and the chosen landing action are still in effect.

**Acceptance Scenarios**:

1. **Given** a user with no prior preferences, **When** they select a theme, **Then** the theme is persisted to their user record and applied on their next login.
2. **Given** a user with no prior preferences, **When** they set their default landing action to "new chat", **Then** that choice is persisted.
3. **Given** a user who favorites a persona and sets it as their default landing action, **When** they persist the preference, **Then** the stored preference references that specific persona.
4. **Given** a user who favorites a prompt and sets it as their default landing action, **When** they persist the preference, **Then** the stored preference references that specific prompt.
5. **Given** a user with an existing theme/landing-action preference, **When** they change only the theme, **Then** the landing-action preference is unaffected, and vice versa.

---

### User Story 2 - Resolve the landing action with self-healing fallback (Priority: P1)

On login, the system resolves the user's stored landing-action preference into an actual screen: a new chat, a direct chat with the referenced favorite persona, or a chat pre-seeded from the referenced favorite prompt. If the preference points at a persona or prompt that no longer exists, is no longer favorited, or is no longer accessible to the user, the system falls back to a new chat instead of erroring — and corrects the stored preference so the same broken resolution isn't re-attempted on the next load.

**Why this priority**: This is the requirement with explicit failure-mode language in the PRD ("self-heal") and is equally foundational to Story 1 — a landing-action preference that isn't safely resolvable is worse than no preference at all, since it could otherwise strand or error out a user on every login. Ranked P1 alongside Story 1.

**Independent Test**: Set a favorite-persona (or favorite-prompt) landing action, then delete/unfavorite/revoke access to that resource out of band, then log in and confirm the user lands on a new chat with no error, and that the stored preference has been updated to "new chat" so a subsequent login resolves directly without re-checking the deleted resource.

**Acceptance Scenarios**:

1. **Given** a stored landing action of "new chat", **When** the user logs in, **Then** a new chat opens.
2. **Given** a stored landing action referencing a favorite persona that still exists, is still favorited, and is still accessible to the user, **When** the user logs in, **Then** a direct chat with that persona opens.
3. **Given** a stored landing action referencing a favorite prompt that still exists, is still favorited, and is still accessible to the user, **When** the user logs in, **Then** a chat pre-seeded with that prompt's content opens.
4. **Given** a stored landing action referencing a favorite persona that has since been deleted, **When** the user logs in, **Then** the system falls back to a new chat, no error is surfaced, and the stored preference is updated to "new chat".
5. **Given** a stored landing action referencing a persona that still exists but has been unfavorited or is no longer accessible to the user (e.g., unshared), **When** the user logs in, **Then** the system falls back to a new chat and updates the stored preference the same way as scenario 4.
6. **Given** a stored landing action referencing a favorite prompt that has since been deleted, unfavorited, or made inaccessible, **When** the user logs in, **Then** the system falls back to a new chat and updates the stored preference the same way as scenario 4.
7. **Given** a preference that was already self-healed to "new chat" on a prior login (per scenario 4), **When** the user logs in again, **Then** the system resolves directly to a new chat without re-attempting resolution of the deleted resource.

### Edge Cases

- What happens when the referenced persona/prompt exists and is accessible but the favorite itself was removed while the resource remains favorited by other users? (Resolution is per-user; the fallback in Story 2 must trigger because the requirement is that the *user's* favorite still references it, not merely that the resource exists.)
- How does the system handle a race where the referenced persona/prompt is deleted concurrently with the login request itself resolving it?
- What happens on a user's very first login before any preference record exists? (Must behave identically to an explicit "new chat" preference, not error.)
- What happens when a user's theme preference value is invalid or corrupted (e.g., neither "light" nor "dark")? (Falls back to a default theme; does not block login.)

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST persist a per-user theme preference (`light` | `dark`). *(REQ-PREF-1)*
- **FR-002**: The system MUST persist a per-user default landing-action preference of one of three kinds: new chat, a reference to a specific favorite persona, or a reference to a specific favorite prompt. *(REQ-PREF-1)*
- **FR-003**: Updating the theme preference MUST NOT alter the stored landing-action preference, and updating the landing-action preference MUST NOT alter the stored theme preference. *(REQ-PREF-1)*
- **FR-004**: A user with no prior stored preference MUST be treated as having a theme of the system default and a landing action of "new chat", without error. *(REQ-PREF-1)*
- **FR-005**: On login, the system MUST resolve the stored landing-action preference into: a new chat (for "new chat"), a direct chat with the referenced persona (for a favorite-persona reference), or a chat pre-seeded with the referenced prompt's content (for a favorite-prompt reference), consistent with the prompt-launch mechanic defined in FR-018 of `specs/016-prompt-crud-sharing-ownership-transfer/spec.md`. *(REQ-PREF-2)*
- **FR-006**: Resolution of a favorite-persona or favorite-prompt landing action MUST verify, at resolution time, that the referenced resource still exists, is still favorited by the user, and is still accessible to the user under that resource's own access rules (per `specs/009-persona-crud-authorization/spec.md` for personas, `specs/016-prompt-crud-sharing-ownership-transfer/spec.md` for prompts). *(REQ-PREF-2)*
- **FR-007**: If any check in FR-006 fails, the system MUST fall back to opening a new chat instead of erroring or leaving the user unable to log in. *(REQ-PREF-2)*
- **FR-008**: Whenever the fallback in FR-007 occurs, the system MUST update the user's stored landing-action preference to "new chat" so that subsequent logins resolve directly without re-evaluating the stale reference. *(REQ-PREF-2)*
- **FR-009**: An invalid or unrecognized stored theme value MUST fall back to the system default theme without blocking login. *(REQ-PREF-2)*
- **FR-010**: This spec does not define the favorite-persona or favorite-prompt entities themselves, or the rules for who may favorite what — those are owned by `specs/009-persona-crud-authorization/spec.md` and `specs/016-prompt-crud-sharing-ownership-transfer/spec.md` respectively; this spec only stores a reference to a favorite and resolves it at login.

### Key Entities *(include if feature involves data)*

- **UserPreference**: per-user record — `userId`, `theme` (`light` | `dark`), `landingAction` (`new-chat` | reference to a favorited `PersonaModel` | reference to a favorited `PromptModel`). Updated in place by self-healing resolution (FR-008); does not duplicate the favorite lists themselves, which live on `PersonaFavorite`/`PromptFavorite`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of theme and landing-action preference changes persist across a new login session in the test suite.
- **SC-002**: 100% of logins with a valid landing-action preference (new chat, favorite persona, favorite prompt) resolve to the correct destination.
- **SC-003**: 100% of logins with a landing-action preference referencing a deleted, unfavorited, or inaccessible persona/prompt fall back to a new chat with 0 errors surfaced to the user.
- **SC-004**: 100% of self-healed preferences (per SC-003) resolve directly to "new chat" on the next login, with 0 repeated re-checks of the stale reference.

## Assumptions

- "Favorite persona" and "favorite prompt" reuse the existing per-user favorites entities (`PersonaFavorite`, `PromptFavorite`) defined in specs 009 and 016; this spec does not introduce a second favoriting mechanism.
- Accessibility checks for a referenced persona/prompt at resolution time reuse each resource's own existing authorization rules (specs 009, 016, and 018 where applicable) rather than introducing a preferences-specific access check.
- "Self-heal" (FR-008) means correcting the stored preference to "new chat" on first detection of staleness; it does not require notifying the user or offering to pick a replacement favorite, which is out of scope for this spec.
- Theme values are limited to `light`/`dark` per the PRD's explicit wording; a "system/auto" theme option is out of scope unless added in a future spec.
