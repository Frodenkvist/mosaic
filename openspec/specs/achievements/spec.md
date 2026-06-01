# achievements Specification

## Purpose
TBD - created by archiving change track-achievements. Update Purpose after archive.
## Requirements
### Requirement: Link a game to a Steam achievement schema

The system SHALL allow a game to be associated with a Steam application id (appid) that identifies its achievement schema, SHALL propose a likely appid by matching the game's name but SHALL NOT link it without user confirmation, and SHALL allow the user to set the appid manually or declare that the game has no Steam achievements.

#### Scenario: Propose an appid by name match

- **WHEN** the user requests achievement linking for a game that has no appid set
- **THEN** the system SHALL match the game's name to propose a candidate Steam appid and SHALL present it for the user to confirm rather than linking automatically

#### Scenario: Manual appid override

- **WHEN** the user enters or corrects a Steam appid for a game
- **THEN** the system SHALL use the user-supplied appid as the authoritative link and SHALL persist it with the game

#### Scenario: Game has no Steam achievements

- **WHEN** the user declares that a game has no Steam achievements
- **THEN** the system SHALL leave the game unlinked and SHALL NOT attempt Steam schema resolution for it

### Requirement: Resolve achievement definitions from the Steam Web API

The system SHALL resolve a linked game's achievement definitions (stable key, display name, description, unlocked/locked icons, and hidden flag) from the Steam Web API using the game's appid and a user-supplied Steam Web API key, SHALL cache the definitions and icons locally, and SHALL re-resolve only on initial link or an explicit refresh rather than on every launch.

#### Scenario: Fetch and cache definitions for a linked game

- **WHEN** a game is linked to an appid and a Steam Web API key is configured
- **THEN** the system SHALL fetch the achievement schema, persist the definitions, cache the icon images in the application data directory, and make the achievement list available for that game

#### Scenario: Refresh definitions on request

- **WHEN** the user requests an achievement refresh for a linked game
- **THEN** the system SHALL re-fetch the schema and update the stored definitions while preserving the game's existing unlock state

#### Scenario: No Steam Web API key configured

- **WHEN** no Steam Web API key is configured
- **THEN** the system SHALL disable automatic schema resolution, SHALL surface that resolution is unavailable, and SHALL continue to allow manual achievement marking

### Requirement: Detect achievement unlocks from local Steam-emulator files

The system SHALL detect which of a linked game's achievements are unlocked by locating and parsing the achievement/stat files written by common Steam emulators in their known per-game and per-user locations, and SHALL determine each unlock's timestamp from the file when available.

#### Scenario: Read unlocks from an emulator file

- **WHEN** the system scans a linked game that has a recognized Steam-emulator achievement file
- **THEN** the system SHALL parse the unlocked achievements and their timestamps and SHALL mark the corresponding definitions as unlocked

#### Scenario: No recognized achievement file present

- **WHEN** the system scans a linked game and finds no recognized emulator achievement file
- **THEN** the system SHALL report no automatic unlocks for that game and SHALL leave any existing unlock state unchanged

#### Scenario: Unlock timestamp unavailable

- **WHEN** an emulator file marks an achievement unlocked but provides no usable timestamp
- **THEN** the system SHALL record the unlock using the detection time

### Requirement: Notify achievement unlocks during a play session

The system SHALL watch a linked game's emulator achievement files for changes while a play session for that game is active, SHALL raise an in-application notification for each newly-unlocked achievement in real time, SHALL persist each new unlock, and SHALL perform a final reconciling scan when the session ends so that an unlock is not lost if a file change is missed while watching.

#### Scenario: Achievement unlocked while playing

- **WHEN** a tracked game's emulator achievement file changes during an active play session to mark an achievement newly unlocked
- **THEN** the system SHALL persist the unlock and SHALL raise an "achievement unlocked" notification for that achievement

#### Scenario: Reconcile on session end

- **WHEN** a tracked game's play session ends
- **THEN** the system SHALL stop watching that game's files and SHALL perform a final scan that persists any unlocks not already captured during the session

#### Scenario: No active watcher for an unlinked game

- **WHEN** a play session starts for a game that is not linked or has achievement tracking disabled
- **THEN** the system SHALL NOT start an achievement watcher for that session

### Requirement: Manually mark achievements

The system SHALL allow the user to mark a game's achievements unlocked or locked by hand, SHALL persist manual marks alongside automatically-detected unlocks, and SHALL allow user-defined achievements for games that have no resolvable schema.

#### Scenario: Manually toggle an achievement

- **WHEN** the user marks an achievement unlocked or locked
- **THEN** the system SHALL persist the change and SHALL reflect it in the game's progress

#### Scenario: Define achievements without a schema

- **WHEN** a game has no resolvable Steam schema and the user adds an achievement definition manually
- **THEN** the system SHALL persist the user-defined achievement and allow it to be marked unlocked or locked

### Requirement: Unlocks are monotonic and manual marks are preserved

The system SHALL treat unlocks as monotonic: once an achievement is recorded unlocked it SHALL NOT be automatically re-locked by a later scan in which it is absent, and an automatic schema refresh SHALL NOT discard manually-recorded unlock state.

#### Scenario: Re-scan does not re-lock

- **WHEN** a later automatic scan does not include an achievement that was previously recorded as unlocked
- **THEN** the system SHALL keep that achievement unlocked

#### Scenario: Refresh preserves manual unlocks

- **WHEN** the achievement definitions are refreshed from the Steam Web API
- **THEN** the system SHALL preserve the game's existing unlock state, including manually-marked unlocks

### Requirement: Configure achievement tracking per game

The system SHALL allow each game to have achievement tracking enabled or disabled and its source mode configured (automatic detection or manual only), and SHALL persist this configuration with the game.

#### Scenario: Disable tracking for a game

- **WHEN** the user disables achievement tracking for a game
- **THEN** the system SHALL NOT resolve schema or watch files for that game and SHALL persist the disabled state

#### Scenario: Force manual-only mode

- **WHEN** the user sets a game to manual-only achievement mode
- **THEN** the system SHALL NOT perform automatic emulator-file detection for that game while still allowing manual marking

### Requirement: Persist achievements and derive progress

The system SHALL persist achievement definitions and unlock state to local storage so they are restored on restart, SHALL derive each game's progress (unlocked count out of total) from the stored data rather than storing it denormalized, and SHALL delete a game's achievements, unlock state, and cached achievement icons when the game is removed.

#### Scenario: Achievements restored on restart

- **WHEN** the application is restarted
- **THEN** the system SHALL load each linked game's stored achievement definitions and unlock state

#### Scenario: Progress derived from stored data

- **WHEN** a game's achievement progress is requested
- **THEN** the system SHALL return the count of unlocked achievements out of the total defined for that game, computed from stored data

#### Scenario: Removal cleans up achievement data

- **WHEN** a game is removed from the library
- **THEN** the system SHALL delete that game's achievement definitions, unlock state, and cached icon files, and SHALL NOT delete files outside the application data directory

