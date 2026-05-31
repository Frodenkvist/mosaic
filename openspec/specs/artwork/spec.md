# artwork Specification

## Purpose
TBD - created by archiving change game-library-mvp. Update Purpose after archive.
## Requirements
### Requirement: Auto-fetch artwork from SteamGridDB
The system SHALL fetch cover/grid, hero, and logo artwork for a game from SteamGridDB when an API key is configured.

#### Scenario: Fetch artwork for a newly added game
- **WHEN** a game is added and a SteamGridDB API key is configured
- **THEN** the system SHALL asynchronously search SteamGridDB by the game's name and download available artwork without blocking the user interface

#### Scenario: Fetch does not block adding the game
- **WHEN** artwork is being fetched for a game
- **THEN** the game SHALL already be present and usable in the library, with artwork appearing once retrieved

### Requirement: Cache artwork locally
The system SHALL cache downloaded artwork in local storage and reference it by local path so artwork is not re-downloaded unnecessarily.

#### Scenario: Reuse cached artwork
- **WHEN** a game already has cached artwork
- **THEN** the system SHALL use the cached local image and SHALL NOT re-download it

### Requirement: Graceful degradation without an API key
The system SHALL function when no SteamGridDB API key is configured.

#### Scenario: No API key configured
- **WHEN** no SteamGridDB API key is configured
- **THEN** the system SHALL skip automatic artwork fetching, display a placeholder for games without artwork, and SHALL NOT block any library or play-tracking functionality

#### Scenario: Fetch failure leaves a placeholder
- **WHEN** an artwork fetch fails or returns no match
- **THEN** the system SHALL display a placeholder image and SHALL NOT prevent the game from being used

### Requirement: Manual artwork override
The system SHALL allow the user to set a local image as a game's artwork, overriding auto-fetched artwork.

#### Scenario: User sets custom artwork
- **WHEN** the user selects a local image for a game's artwork slot
- **THEN** the system SHALL use that image as the game's artwork

#### Scenario: Override is preserved
- **WHEN** a game has a manually set artwork override
- **THEN** automatic artwork fetching SHALL NOT replace the user's override

### Requirement: Smart match resolution
The system SHALL resolve the SteamGridDB game using multiple name candidates and select the best match by name similarity rather than the first result.

#### Scenario: Search candidates derived from name and folders
- **WHEN** resolving artwork for a game
- **THEN** the system SHALL try candidate search terms in priority order: a user-given name, then the install-folder names (skipping generic folders such as `bin`, `win64`), then the executable name

#### Scenario: Best match chosen by name similarity
- **WHEN** a search term returns multiple results
- **THEN** the system SHALL score results by diacritic-insensitive token similarity and SHALL prefer the result that most completely covers the search term (e.g. "God of War Ragnarök" over the base "God of War"), accepting only matches above a confidence threshold

#### Scenario: Prefer a match that has cover art
- **WHEN** several candidate games match the name equally well but some have no cover art
- **THEN** the system SHALL select a candidate that actually has cover (grid) art

#### Scenario: CamelCase last-resort fallback
- **WHEN** no candidate matches by the normal terms
- **THEN** the system SHALL retry with CamelCase/PascalCase-split variants of the names (e.g. "HatinTime" → "Hatin Time") under a relaxed threshold, still requiring cover art

### Requirement: Adopt the matched title as the game name
The system SHALL replace a path-derived game name with the matched SteamGridDB title, while leaving a user-typed name unchanged.

#### Scenario: Auto-derived name is replaced
- **WHEN** a game whose name matches its executable or a folder in its path is matched to a SteamGridDB title on the initial fetch
- **THEN** the system SHALL set the game's name to the matched title

#### Scenario: Custom name is preserved
- **WHEN** the game's name does not correspond to any path component, or the operation is a refetch
- **THEN** the system SHALL NOT change the game's name

### Requirement: Throttled fetching and batch refetch
The system SHALL throttle SteamGridDB access so batch operations are not rate-limited, and SHALL provide a way to fetch missing artwork for all games.

#### Scenario: Batch add is not rate-limited
- **WHEN** multiple games are added at once (e.g. after a scan)
- **THEN** the system SHALL serialize SteamGridDB requests so artwork is fetched reliably rather than failing under rate limits

#### Scenario: Refetch missing artwork for all games
- **WHEN** the user invokes "refetch missing artwork"
- **THEN** the system SHALL fetch artwork for every game lacking it, skip games that already have complete artwork, and update the library as each game's artwork arrives

#### Scenario: Refetch a single game
- **WHEN** the user refetches artwork for one game
- **THEN** the system SHALL re-resolve and re-download non-override artwork for that game without changing its name

