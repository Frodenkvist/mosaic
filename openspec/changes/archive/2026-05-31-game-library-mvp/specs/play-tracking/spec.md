## ADDED Requirements

### Requirement: Launch a game from Mosaic
The system SHALL launch a game's executable using its configured launch arguments and working directory.

#### Scenario: Launch with configuration
- **WHEN** the user launches a game from the library
- **THEN** the system starts the game's executable with its configured arguments and working directory

#### Scenario: Missing executable
- **WHEN** the user launches a game whose executable path no longer exists
- **THEN** the system SHALL report an error to the user and SHALL NOT record a play session

### Requirement: Track play time across the full process tree
The system SHALL measure a play session's duration based on the lifetime of the entire process tree launched, not only the initially started process, so that games launched via a launcher that spawns the real game are measured correctly.

#### Scenario: Launcher spawns the real game and exits
- **WHEN** the launched executable spawns one or more child processes and the initial process exits while a descendant is still running
- **THEN** the system SHALL continue the play session until the last process in the tree exits

#### Scenario: Session duration reflects whole-tree lifetime
- **WHEN** the entire launched process tree has exited
- **THEN** the system ends the session and records a duration spanning from launch until the last process exited

### Requirement: Real-executable override for play tracking
The system SHALL support a per-game "real executable" setting that identifies the process to track when it differs from the launched executable.

#### Scenario: Track a differently-named real executable
- **WHEN** a game has a configured real-executable name and is launched
- **THEN** the system SHALL determine the session's active period based on the presence of the real executable rather than (or in addition to) the launched process

### Requirement: Record play sessions
The system SHALL record each play session with its start time, end time, and duration, persisted to local storage.

#### Scenario: Session persisted on game exit
- **WHEN** a tracked game's process tree exits
- **THEN** the system persists a play session record containing the start time, end time, and duration for that game

#### Scenario: Session start recorded immediately
- **WHEN** a game is launched
- **THEN** the system SHALL persist the session's start time immediately so an in-progress session is not lost if the application closes unexpectedly

#### Scenario: Reconcile an unfinished session on startup
- **WHEN** the application starts and finds a persisted session that was never closed
- **THEN** the system SHALL reconcile that session according to a defined policy rather than leaving it permanently open

### Requirement: Derive play statistics
The system SHALL derive per-game total play time and last-played time from recorded play sessions.

#### Scenario: Total play time
- **WHEN** play statistics are requested for a game
- **THEN** the system SHALL return the sum of all recorded session durations for that game

#### Scenario: Last played
- **WHEN** play statistics are requested for a game
- **THEN** the system SHALL return the most recent session end time as the game's last-played time, or indicate the game has never been played if it has no sessions
