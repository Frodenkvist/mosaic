## ADDED Requirements

### Requirement: Surface artwork fetch lifecycle
The system SHALL signal the lifecycle of an artwork fetch — start, success, and failure — so that callers and the user interface can reflect an in-progress state and a failed state, not only a successful change. Success is signalled by the existing artwork-updated signal; start and failure are distinct signals.

#### Scenario: Fetch start is signalled
- **WHEN** an artwork fetch is attempted for a game (a SteamGridDB API key is configured and there is artwork still to fetch)
- **THEN** the system SHALL signal that fetching has started for that game before contacting SteamGridDB

#### Scenario: Successful fetch is signalled as an update
- **WHEN** a fetch resolves a match and changes the game's artwork or name
- **THEN** the system SHALL signal an artwork update for that game (the existing success signal) and SHALL NOT signal a failure

#### Scenario: Failed fetch is signalled
- **WHEN** a fetch is attempted but finds no match, retrieves no usable asset, or errors
- **THEN** the system SHALL signal that fetching failed for that game, distinct from the success signal, and SHALL still leave the game usable with a placeholder

#### Scenario: No signal without an API key
- **WHEN** no SteamGridDB API key is configured
- **THEN** the system SHALL NOT signal a fetch start or a fetch failure, because no fetch is attempted

#### Scenario: No signal when nothing needs fetching
- **WHEN** a fetch is requested for a game that already has the artwork it would fetch
- **THEN** the system SHALL NOT signal a fetch start or failure for the kinds that are already present

#### Scenario: Retry re-signals the lifecycle
- **WHEN** a fetch is retried for a game that previously failed
- **THEN** the system SHALL signal a fetch start again and then signal success or failure according to the new attempt
