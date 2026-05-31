## ADDED Requirements

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
