## ADDED Requirements

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

## MODIFIED Requirements

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
