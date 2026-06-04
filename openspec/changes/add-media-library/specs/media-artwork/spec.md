## ADDED Requirements

### Requirement: Auto-fetch posters and metadata from TMDB

The system SHALL fetch a poster and backdrop image and descriptive metadata (such as the matched title, year, and overview) for a movie or series from TMDB when a TMDB API key is configured, asynchronously and without blocking the user interface.

#### Scenario: Fetch artwork for a newly added media item

- **WHEN** a media item is added and a TMDB API key is configured
- **THEN** the system SHALL asynchronously search TMDB by the item's title (and year when known) and download available poster/backdrop artwork without blocking the user interface

#### Scenario: Fetch does not block adding the media

- **WHEN** artwork is being fetched for a media item
- **THEN** the media item SHALL already be present and usable in the library, with artwork appearing once retrieved

### Requirement: Match media by title and year

The system SHALL resolve the TMDB entry for a media item by its title, SHALL use a parsed release year to disambiguate when available, and SHALL select the best match by name similarity rather than the first result.

#### Scenario: Year disambiguates same-titled entries

- **WHEN** multiple TMDB entries share a title and the media item has a parsed year
- **THEN** the system SHALL prefer the entry whose release year matches

#### Scenario: Best match chosen by name similarity

- **WHEN** a search returns multiple results
- **THEN** the system SHALL score results by name similarity and SHALL accept only a match above a confidence threshold, leaving the item without auto artwork otherwise

### Requirement: Fetch per-episode metadata for a series

The system SHALL, for a series matched to TMDB, fetch per-episode metadata (episode title and still image) and associate it with the series' episodes by season and episode number, so that episodes with cryptic filenames become navigable.

#### Scenario: Episode titles filled from TMDB

- **WHEN** a series is matched to TMDB and an episode's season and episode number map to a TMDB episode
- **THEN** the system SHALL associate that TMDB episode's title and still image with the episode

#### Scenario: Unmatched episode keeps its filename-derived title

- **WHEN** an episode's season/episode number has no corresponding TMDB episode
- **THEN** the system SHALL keep the episode's filename-derived title and SHALL NOT block the rest of the series' metadata

### Requirement: Cache media artwork locally

The system SHALL cache downloaded media artwork in local storage and reference it by local path so artwork is not re-downloaded unnecessarily.

#### Scenario: Reuse cached artwork

- **WHEN** a media item already has cached artwork
- **THEN** the system SHALL use the cached local image and SHALL NOT re-download it

### Requirement: Graceful degradation without a TMDB API key

The system SHALL function when no TMDB API key is configured.

#### Scenario: No API key configured

- **WHEN** no TMDB API key is configured
- **THEN** the system SHALL skip automatic artwork and metadata fetching, display a placeholder for media without artwork, and SHALL NOT block scanning, playback, or watch tracking

#### Scenario: Fetch failure leaves a placeholder

- **WHEN** an artwork fetch fails or returns no confident match
- **THEN** the system SHALL display a placeholder image and SHALL NOT prevent the media item from being used

### Requirement: Manual artwork override

The system SHALL allow the user to set a local image as a media item's poster, overriding auto-fetched artwork, and SHALL NOT let automatic fetching replace a manual override.

#### Scenario: User sets a custom poster

- **WHEN** the user selects a local image as a media item's poster
- **THEN** the system SHALL use that image as the item's poster

#### Scenario: Override is preserved

- **WHEN** a media item has a manually set poster
- **THEN** automatic artwork fetching SHALL NOT replace the user's override

### Requirement: Throttled fetching and batch refetch

The system SHALL throttle TMDB access so batch operations (e.g. after a scan) are not rate-limited, and SHALL provide a way to fetch missing artwork for all media.

#### Scenario: Batch scan is not rate-limited

- **WHEN** many media items are added at once after a scan
- **THEN** the system SHALL serialize TMDB requests so artwork is fetched reliably rather than failing under rate limits

#### Scenario: Refetch missing artwork

- **WHEN** the user invokes a refetch of missing artwork
- **THEN** the system SHALL fetch artwork for media items lacking it, skip items that already have artwork or a manual override, and update the library as each item's artwork arrives
