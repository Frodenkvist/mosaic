## MODIFIED Requirements

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

## ADDED Requirements

### Requirement: Achievement detection is observable

The system SHALL make automatic unlock detection observable so that a scan finding no unlocks can be diagnosed. For each automatic unlock scan the system SHALL record a diagnostic describing which candidate file locations were considered, which of them existed and were parsed, how many parsed achievement keys were found, how many matched the game's stored definitions, and how many were skipped as unmatched. The system SHALL log this diagnostic, SHALL make a concise summary of it available to the caller so it can be surfaced to the user, and SHALL log — rather than silently swallow — recoverable errors encountered while locating, reading, or parsing emulator files.

#### Scenario: Scan records a diagnostic

- **WHEN** the system performs an automatic unlock scan for a linked game
- **THEN** the system SHALL record and log a diagnostic that includes the candidate locations considered, which existed, and the matched and unmatched key counts

#### Scenario: Diagnostic available when no unlocks found

- **WHEN** an automatic unlock scan finds no new unlocks
- **THEN** the system SHALL make available a concise summary stating whether any recognized file was found and how many keys (if any) were unmatched, sufficient to explain why nothing registered

#### Scenario: Recoverable error is logged not swallowed

- **WHEN** a recoverable error occurs while locating, reading, or parsing a game's emulator achievement files
- **THEN** the system SHALL log the error and continue, rather than discarding it silently
