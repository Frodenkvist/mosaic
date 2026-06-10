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

The system SHALL detect which of a linked game's achievements are unlocked by locating and parsing the achievement/stat files written by common Steam emulators across their known per-game and per-user locations — including emulator save-redirect locations and additional per-user roots beyond a single fixed default — and SHALL determine each unlock's timestamp from the file when available. When matching a parsed achievement key to a stored achievement definition, the system SHALL match the key to the definition's stable key **case-insensitively** so that a difference in letter casing does not prevent a match. A parsed unlock whose key matches no stored definition SHALL be counted and recorded in the scan diagnostic rather than silently discarded.

#### Scenario: Read unlocks from an emulator file

- **WHEN** the system scans a linked game that has a recognized Steam-emulator achievement file
- **THEN** the system SHALL parse the unlocked achievements and their timestamps and SHALL mark the corresponding definitions as unlocked

#### Scenario: Recognized file in a non-default location

- **WHEN** a linked game's recognized emulator achievement file exists in a known save-redirect or alternate per-user location rather than the single historical default path
- **THEN** the system SHALL still locate and parse it and SHALL mark the corresponding definitions as unlocked

#### Scenario: Case-insensitive key match

- **WHEN** an emulator file marks an achievement unlocked using a key whose letter casing differs from the stored definition's key
- **THEN** the system SHALL match it to that definition case-insensitively and SHALL mark the definition unlocked

#### Scenario: No recognized achievement file present

- **WHEN** the system scans a linked game and finds no recognized emulator achievement file
- **THEN** the system SHALL report no automatic unlocks for that game and SHALL leave any existing unlock state unchanged

#### Scenario: Unlock timestamp unavailable

- **WHEN** an emulator file marks an achievement unlocked but provides no usable timestamp
- **THEN** the system SHALL record the unlock using the detection time

#### Scenario: Detected unlock with no matching definition

- **WHEN** an emulator file marks an achievement unlocked under a key that matches no stored definition for the game
- **THEN** the system SHALL NOT silently discard it but SHALL count it in the scan diagnostic as an unmatched key

### Requirement: Notify achievement unlocks during a play session

The system SHALL watch a linked game's emulator achievement files for changes while a play session for that game is active, SHALL raise an in-application notification for each newly-unlocked achievement in real time, SHALL persist each new unlock, and SHALL perform a final reconciling scan when the session ends — retrying that scan for a brief bounded period to tolerate an emulator that flushes its achievement file as the game exits — so that an unlock is not lost if a file change is missed while watching or is written at shutdown.

#### Scenario: Achievement unlocked while playing

- **WHEN** a tracked game's emulator achievement file changes during an active play session to mark an achievement newly unlocked
- **THEN** the system SHALL persist the unlock and SHALL raise an "achievement unlocked" notification for that achievement

#### Scenario: Reconcile on session end

- **WHEN** a tracked game's play session ends
- **THEN** the system SHALL stop watching that game's files and SHALL perform a final scan that persists any unlocks not already captured during the session

#### Scenario: Unlock written as the game exits

- **WHEN** a tracked game writes a newly-unlocked achievement to its emulator file at or just after the moment its play session ends
- **THEN** the bounded reconcile retry SHALL still detect and persist that unlock rather than losing it to the write-versus-scan race

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

### Requirement: Achievement detection is observable

The system SHALL make automatic unlock detection observable so that a scan finding no unlocks can be diagnosed. For each automatic unlock scan the system SHALL record a diagnostic describing which candidate file locations were considered, which of them existed and were parsed, **and — for each candidate — whether its containing directory (the emulator save folder) existed**, how many parsed achievement keys were found, how many matched the game's stored definitions, and how many were skipped as unmatched. When a scan finds no recognized achievement file, the system's concise summary SHALL distinguish the case where a known emulator save folder exists but contains no achievements file (indicating the emulator has no achievement schema and recorded nothing) from the case where no candidate save folder was found at all. The system SHALL log this diagnostic, SHALL make a concise summary of it available to the caller so it can be surfaced to the user, and SHALL log — rather than silently swallow — recoverable errors encountered while locating, reading, or parsing emulator files.

#### Scenario: Scan records a diagnostic

- **WHEN** the system performs an automatic unlock scan for a linked game
- **THEN** the system SHALL record and log a diagnostic that includes the candidate locations considered, which existed, whether their containing directories existed, and the matched and unmatched key counts

#### Scenario: Diagnostic available when no unlocks found

- **WHEN** an automatic unlock scan finds no new unlocks
- **THEN** the system SHALL make available a concise summary stating whether any recognized file was found and how many keys (if any) were unmatched, sufficient to explain why nothing registered

#### Scenario: Save folder exists but holds no achievements file

- **WHEN** an automatic unlock scan finds no recognized achievements file, but at least one candidate's containing emulator save folder exists
- **THEN** the concise summary SHALL state that an emulator save folder was found without an achievements file and SHALL indicate that the emulator likely has no achievement schema configured

#### Scenario: No emulator save folder found

- **WHEN** an automatic unlock scan finds no recognized achievements file and no candidate's containing emulator save folder exists, disregarding the game's own install folder (which always exists)
- **THEN** the concise summary SHALL state that no emulator save folder was found, indicating the game may not have run under a supported emulator or uses one the system does not recognize

#### Scenario: Recoverable error is logged not swallowed

- **WHEN** a recoverable error occurs while locating, reading, or parsing a game's emulator achievement files
- **THEN** the system SHALL log the error and continue, rather than discarding it silently

### Requirement: Generate a Steam-emulator achievement schema

The system SHALL be able to generate a Steam-emulator achievement schema file for a linked game and place it where the emulator reads it, so that an emulator shipped without an achievement schema will recognize and persist *future* achievement unlocks. The system SHALL derive the schema from the game's resolved achievement definitions, fetching them from the Steam Web API when none are stored and a key is configured. The system SHALL write the schema in the emulator's expected format (a JSON array of achievement objects, each carrying at minimum the achievement's stable key plus its display name, description, and hidden flag) into the game's own emulator settings folder (the `steam_settings` folder beside the game executable). The system SHALL NOT write outside the game folder, and SHALL NOT overwrite an existing schema file without explicit user confirmation. Achievements that were earned before the schema existed SHALL NOT be backfilled by this operation.

#### Scenario: Generate schema for a linked game with definitions

- **WHEN** the user requests schema generation for a game linked to a Steam App ID that has resolved achievement definitions
- **THEN** the system SHALL write an emulator achievement schema file containing those definitions, in the emulator's expected JSON-array format, into the game's `steam_settings` folder, and SHALL report the path it wrote

#### Scenario: Definitions cannot be resolved

- **WHEN** the user requests schema generation for a game that is not linked to a Steam App ID, or that has no stored achievement definitions and no Steam Web API key configured to fetch them
- **THEN** the system SHALL NOT write a file and SHALL report that achievement definitions could not be resolved (the game needs linking, a refresh, or a Steam Web API key)

#### Scenario: Existing schema not overwritten without confirmation

- **WHEN** a schema file already exists at the target location
- **THEN** the system SHALL require explicit user confirmation before overwriting it and SHALL leave the existing file unchanged if confirmation is declined

#### Scenario: Generated schema is written in the emulator format

- **WHEN** the system writes a generated schema
- **THEN** each achievement object SHALL use the emulator's field names with the hidden flag serialized as the emulator expects (the string `"0"` or `"1"`), so the emulator parses it without further editing

#### Scenario: Generated schema enables subsequent unlock detection

- **WHEN** a schema has been generated and the game afterwards records achievement unlocks through the emulator
- **THEN** the existing emulator-file scan and live-watch path SHALL detect and persist those unlocks without any further configuration

