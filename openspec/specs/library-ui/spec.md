# library-ui Specification

## Purpose
TBD - created by archiving change game-library-mvp. Update Purpose after archive.
## Requirements
### Requirement: Display the game library
The system SHALL display the user's games in a browsable view showing each game's artwork and name.

#### Scenario: Library shows all games
- **WHEN** the user opens the library view
- **THEN** the system displays every game in the library with its artwork (or a placeholder) and name

#### Scenario: Empty library guidance
- **WHEN** the library contains no games
- **THEN** the system SHALL display guidance prompting the user to add or scan for games

### Requirement: Show game details
The system SHALL provide a detail view for a selected game showing its play statistics and launch action.

#### Scenario: View game details
- **WHEN** the user selects a game
- **THEN** the system displays the game's name, artwork, total play time, last-played time, and a launch action

#### Scenario: Launch from detail view
- **WHEN** the user activates the launch action in a game's detail view
- **THEN** the system launches the game and begins play tracking

### Requirement: Recently played view
The system SHALL provide a recently-played view listing games ordered by their most recent play time.

#### Scenario: Order by last played
- **WHEN** the user opens the recently-played view
- **THEN** the system lists games that have been played, ordered from most recently played to least recently played

#### Scenario: Reflect a completed session
- **WHEN** a play session completes
- **THEN** the affected game SHALL appear at the top of the recently-played view and its displayed total play time and last-played time SHALL reflect the new session

### Requirement: Consistent dark theme and window chrome
The system SHALL present a consistent dark theme across all controls and windows, including the title bar and application icon.

#### Scenario: All controls follow the dark theme
- **WHEN** any window or dialog is shown
- **THEN** its controls (buttons, text boxes, lists, combo boxes, scrollbars, context menus) and its title bar SHALL use the dark theme rather than default light chrome

#### Scenario: Custom title bar with app identity
- **WHEN** a window is shown
- **THEN** it SHALL display a custom title bar with the Mosaic logo and themed minimize/maximize/close controls, and the window/taskbar SHALL use the Mosaic icon

### Requirement: Library browsing controls
The system SHALL provide search, sort, and per-game context actions in the library.

#### Scenario: Search and sort
- **WHEN** the user types in the search box or changes the sort option
- **THEN** the library SHALL filter by name and order by the chosen criterion (name, most played, recently played, recently added)

#### Scenario: Per-game context actions
- **WHEN** the user right-clicks a game (or uses its action control)
- **THEN** the system SHALL offer Play, Details/Edit, and Remove actions

### Requirement: Live running feedback
The system SHALL indicate when a game is running.

#### Scenario: Running badge with elapsed time
- **WHEN** a game is launched and is running
- **THEN** the system SHALL show a running badge with a live elapsed timer on the game and SHALL disable its Play action until it exits

### Requirement: Usable dialogs
The system SHALL keep dialog content reachable and provide clear save semantics.

#### Scenario: Dialogs scroll when content does not fit
- **WHEN** a dialog (add game, game detail) is smaller than its content
- **THEN** the content SHALL scroll so no control is unreachable

#### Scenario: Save and close
- **WHEN** the user saves changes in the game detail window
- **THEN** the system SHALL persist the changes and close the window, unless the save fails (e.g. duplicate executable), in which case it SHALL stay open and report the error

### Requirement: Artwork fetch feedback
The system SHALL indicate, per game in the library views, when its artwork/name fetch is in progress and when that fetch has failed, complementing the success that is already visible through the updated artwork and name. This feedback is in-session only and applies to the library and recently-played views.

#### Scenario: In-progress indicator while fetching
- **WHEN** a game's artwork fetch is in progress
- **THEN** the system SHALL show an in-progress indicator on that game's tile

#### Scenario: Success clears the indicator
- **WHEN** the fetch for a game succeeds
- **THEN** the system SHALL remove any in-progress indicator and display the fetched artwork and name, without showing a failed indicator

#### Scenario: Failed indicator with retry
- **WHEN** the fetch for a game fails (no match or error)
- **THEN** the system SHALL show a failed indicator on that game's tile, while still displaying the placeholder artwork, and SHALL offer a way to retry the fetch from that tile

#### Scenario: Retry returns the tile to in-progress
- **WHEN** the user activates the retry action on a failed tile
- **THEN** the system SHALL re-attempt the fetch and return the tile to the in-progress state

#### Scenario: No indicator without an API key
- **WHEN** no SteamGridDB API key is configured
- **THEN** the system SHALL NOT show an in-progress or failed indicator and SHALL display the placeholder as before

#### Scenario: Feedback does not persist across restarts
- **WHEN** the application is restarted
- **THEN** the system SHALL NOT show a failed indicator from a previous session; the feedback reflects only the current session's fetch attempts

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

