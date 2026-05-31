## ADDED Requirements

### Requirement: Add a game manually
The system SHALL allow the user to add a game to the library by selecting an executable file and providing a display name.

#### Scenario: Add game by picking an executable
- **WHEN** the user chooses to add a game and selects an executable file
- **THEN** the system creates a game entry with the chosen executable path and a display name (defaulting to the file name) and persists it to the library

#### Scenario: Optional launch configuration
- **WHEN** the user adds or edits a game
- **THEN** the system SHALL allow optionally specifying launch arguments, a working directory, and a "real executable" name used for play tracking

#### Scenario: Reject duplicate executable
- **WHEN** the user attempts to add a game whose executable path already exists in the library
- **THEN** the system SHALL warn the user and SHALL NOT create a duplicate entry

### Requirement: Scan folders for games
The system SHALL allow the user to add candidate games by scanning user-configured folders for executables, with user confirmation before any game is added.

#### Scenario: Scan surfaces candidates for confirmation
- **WHEN** the user scans a configured folder
- **THEN** the system enumerates executable files, filters out known non-game executables (e.g. uninstallers, crash handlers, redistributables), and presents the remaining candidates to the user

#### Scenario: User confirms which candidates to add
- **WHEN** the system presents scan candidates
- **THEN** the system SHALL add only the candidates the user explicitly confirms, and SHALL NOT add any game without confirmation

#### Scenario: Skip already-known executables
- **WHEN** a scanned executable path already exists in the library
- **THEN** the system SHALL exclude it from the candidate list

### Requirement: Scan groups executables by game folder
The system SHALL present at most one candidate per game folder when scanning, choosing the most game-like executable, so a folder full of helper executables yields a single clean entry.

#### Scenario: One candidate per game folder
- **WHEN** a scanned folder contains a game folder with many executables (the game plus crash reporters, redistributables, shader compilers, launchers, etc.)
- **THEN** the system SHALL group executables by their game folder and present a single candidate per game folder, named after the folder

#### Scenario: Best executable chosen per folder
- **WHEN** selecting the executable for a game folder
- **THEN** the system SHALL exclude junk executables (by name fragments and redistributable subfolders) and choose the remaining executable that best matches the folder name, otherwise the largest binary

#### Scenario: Skip folders already in the library
- **WHEN** any executable in a game folder is already in the library
- **THEN** the system SHALL omit that game folder from the candidate list

### Requirement: Edit a game
The system SHALL allow the user to edit a game's editable metadata.

#### Scenario: Edit game metadata
- **WHEN** the user edits a game's name, executable path, launch arguments, working directory, or real-executable override
- **THEN** the system persists the updated values and reflects them in the library

### Requirement: Remove a game
The system SHALL allow the user to remove a game from the library.

#### Scenario: Remove a game
- **WHEN** the user removes a game
- **THEN** the system deletes the game entry and its associated play sessions and artwork records from the library

#### Scenario: Removal does not delete the executable
- **WHEN** the user removes a game
- **THEN** the system SHALL NOT delete the game's executable or any files on disk outside Mosaic's own data directory

### Requirement: Persist the library
The system SHALL persist all games and their metadata to local storage so the library is restored on application restart.

#### Scenario: Library restored on restart
- **WHEN** the application is restarted
- **THEN** the system loads all previously added games with their metadata from local storage
