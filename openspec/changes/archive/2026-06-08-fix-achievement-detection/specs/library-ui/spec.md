## MODIFIED Requirements

### Requirement: Show the achievement list in the game detail view

The game detail view SHALL present the game's achievements with their unlocked/locked state, icons, and unlock timestamps where known, SHALL mask the name and description of hidden achievements until they are unlocked, and SHALL provide controls to manually toggle an achievement, to refresh achievements from the Steam Web API, and to scan for unlocks. When a manual scan finds no new unlocks, the view SHALL surface a brief diagnostic explaining why (such as the number of candidate locations searched, whether a recognized achievement file was found, and the count of detected keys that matched no definition) rather than only reporting that nothing was found.

#### Scenario: List achievements with state

- **WHEN** the user opens the detail view of a game that has achievements
- **THEN** the view SHALL list each achievement with its unlocked or locked state, icon, and unlock time when known

#### Scenario: Hidden achievement masked until unlocked

- **WHEN** the detail view shows a hidden achievement that is still locked
- **THEN** the view SHALL mask its name and description until it is unlocked

#### Scenario: Manual toggle from the detail view

- **WHEN** the user toggles an achievement's unlocked/locked state in the detail view
- **THEN** the view SHALL reflect the new state and the change SHALL be persisted

#### Scenario: Refresh achievements from the detail view

- **WHEN** the user invokes "refresh achievements" for a linked game
- **THEN** the system SHALL re-resolve the achievement definitions and the view SHALL reflect the updated list while preserving unlock state

#### Scenario: Scan with no unlocks surfaces a diagnostic

- **WHEN** the user invokes "scan for unlocks" and the scan finds no new unlocks
- **THEN** the view SHALL surface a brief diagnostic explaining why nothing registered (locations searched, whether a recognized file was found, unmatched-key count) rather than only reporting "No new unlocks found"
