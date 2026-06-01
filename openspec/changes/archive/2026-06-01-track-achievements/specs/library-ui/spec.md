## ADDED Requirements

### Requirement: Show achievement progress on game tiles

The library and recently-played grids SHALL show a per-game achievement progress indicator (unlocked out of total) for games that have achievement definitions, placed so it does not collide with or block the existing running and artwork-fetch indicators, and SHALL omit the indicator for games with no achievements.

#### Scenario: Tile shows progress for a game with achievements

- **WHEN** a game tile is displayed for a game that has achievement definitions
- **THEN** the tile SHALL show its unlocked-out-of-total achievement progress

#### Scenario: No indicator without achievements

- **WHEN** a game has no achievement definitions
- **THEN** the tile SHALL NOT show an achievement progress indicator

#### Scenario: Progress reflects new unlocks

- **WHEN** a game's unlock count changes
- **THEN** the displayed progress SHALL update to reflect the new unlocked-out-of-total count

### Requirement: Show the achievement list in the game detail view

The game detail view SHALL present the game's achievements with their unlocked/locked state, icons, and unlock timestamps where known, SHALL mask the name and description of hidden achievements until they are unlocked, and SHALL provide controls to manually toggle an achievement and to refresh achievements from the Steam Web API.

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

### Requirement: Live achievement-unlock notification

The application SHALL show an in-application notification when an achievement is unlocked during an active play session, identifying the achievement (and its game), reusing the established background-thread-to-UI marshalling used for play-session and artwork events.

#### Scenario: Notification on live unlock

- **WHEN** an achievement is detected as newly unlocked during an active play session
- **THEN** the application SHALL show an in-application notification identifying the unlocked achievement
