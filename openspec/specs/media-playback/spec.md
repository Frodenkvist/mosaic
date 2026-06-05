# media-playback Specification

## Purpose
TBD - created by archiving change add-media-library. Update Purpose after archive.
## Requirements
### Requirement: Play a media item with the configured or default player
The system SHALL allow the user to configure a preferred media player and SHALL persist it. When a preferred player is configured and its executable exists, the system SHALL open a movie or episode's video file with that player; otherwise (no preferred player configured, or its executable missing) the system SHALL open the file using the operating system's default file association. The system SHALL NOT bundle or require a specific player.

#### Scenario: Open with the configured player
- **WHEN** a preferred media player is configured and the user plays a movie or episode
- **THEN** the system SHALL launch that configured player with the item's video file

#### Scenario: Open with the default player when none is configured
- **WHEN** no preferred media player is configured and the user plays a movie or episode
- **THEN** the system SHALL launch the item's video file with the operating system's default association

#### Scenario: Configured player missing falls back to the default
- **WHEN** a preferred media player is configured but its executable no longer exists on disk
- **THEN** the system SHALL fall back to the operating system's default association so the item still plays, rather than failing

#### Scenario: Missing file
- **WHEN** the user plays a media item whose video file no longer exists on disk
- **THEN** the system SHALL report an error to the user and SHALL NOT record a watch session

### Requirement: Record watch activity
The system SHALL record that a media item was opened for watching, persisting the start of a watch session immediately, so that recency-based ordering ("recently watched" / "continue watching") can be derived.

#### Scenario: Watch recorded on play
- **WHEN** a media item is successfully opened for watching
- **THEN** the system SHALL persist a watch record for that item with its start time

#### Scenario: Recency derived from watch records
- **WHEN** the most-recently-watched media is requested
- **THEN** the system SHALL order items by their most recent watch record

### Requirement: Mark a media item watched or finished
The system SHALL allow the user to mark a movie or episode as watched (finished) or unwatched, SHALL persist that state, and SHALL provide a convenience action that marks the current episode watched and advances to the next episode in the series.

#### Scenario: Mark watched
- **WHEN** the user marks a media item watched
- **THEN** the system SHALL persist its watched state and reflect it in the library

#### Scenario: Mark unwatched
- **WHEN** the user marks a watched media item unwatched
- **THEN** the system SHALL clear its watched state and reflect it in the library

#### Scenario: Watch-and-advance for episodes
- **WHEN** the user chooses "mark watched and play next" on an episode
- **THEN** the system SHALL mark that episode watched and SHALL select the next unwatched episode of the series as the resume target

### Requirement: Automatically detect completion and resume position where the player reports it
For players that publish playback state to the operating system's system media controls, the system SHALL observe the playback position of an item it launched, SHALL automatically mark the item watched when playback reaches a near-the-end threshold, and SHALL record the latest playback position as the item's in-file resume position. Automatic detection SHALL only set watched state and SHALL NOT clear it. Where no playback state can be correlated to the launched item, the system SHALL fall back to manual watched-marking without error.

#### Scenario: Auto-mark watched near the end
- **WHEN** a launched item's player reports a playback position that reaches the near-the-end completion threshold
- **THEN** the system SHALL mark the item watched automatically and, for an episode, SHALL advance the series' resume target to the next unwatched episode

#### Scenario: Record an in-file resume position
- **WHEN** a launched item's player reports a playback position before the completion threshold
- **THEN** the system SHALL persist that position as the item's resume position so the user can see where they left off (the system surfaces this as information and SHALL NOT command the external player to seek)

#### Scenario: Player publishes no playback state
- **WHEN** the player for a launched item does not publish playback state that can be correlated to the item
- **THEN** the system SHALL not change the item's watched state automatically, and the item's watched state SHALL change only by explicit user action

#### Scenario: Automatic detection never clears watched state
- **WHEN** automatic detection observes a launched item being played only partway through after it was already marked watched
- **THEN** the system SHALL keep the item watched and SHALL NOT clear its watched state automatically

#### Scenario: Ambiguous correlation makes no automatic change
- **WHEN** the launched item cannot be unambiguously correlated to a single reported playback session
- **THEN** the system SHALL make no automatic watched-state or resume-position change for that launch

### Requirement: Derive a series' progress and resume point
The system SHALL derive, for a series, its watched/total episode progress and its **resume point** — the next unwatched episode ordered by season number then episode number — from the episodes' persisted watched state rather than storing them denormalized.

#### Scenario: Series progress derived
- **WHEN** a series' progress is requested
- **THEN** the system SHALL return the count of watched episodes out of the total episodes for that series, computed from stored data

#### Scenario: Resume point is the next unwatched episode
- **WHEN** a series' resume point is requested and at least one episode is unwatched
- **THEN** the system SHALL return the first unwatched episode ordered by season number then episode number

#### Scenario: Fully watched series
- **WHEN** every episode of a series is marked watched
- **THEN** the system SHALL report the series as fully watched and SHALL NOT return a resume episode

### Requirement: Persist watch data and survive restarts
The system SHALL persist watch records, watched state, and any recorded in-file resume position to local storage so they are restored on restart, and SHALL derive watch statistics from this stored data.

#### Scenario: Watch data restored on restart
- **WHEN** the application is restarted
- **THEN** the system SHALL load each media item's watched state, watch history, and resume position

#### Scenario: Watch state independent of the external player
- **WHEN** the external media player is closed, crashes, or is reopened
- **THEN** the system SHALL retain the recorded watch activity and the user-set watched state regardless of the external player's behavior
