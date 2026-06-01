## ADDED Requirements

### Requirement: Artwork fetch feedback
The system SHALL indicate, per game in the library views, when its artwork/name fetch is in progress and when that fetch has failed, complementing the success that is already visible through the updated artwork and name. This feedback is in-session only and applies to the library and recently-played views.

#### Scenario: In-progress indicator while fetching
- **WHEN** a game's artwork fetch is in progress
- **THEN** the system SHALL show an in-progress indicator on that game's tile

#### Scenario: Success clears the indicator
- **WHEN** the fetch for a game succeeds
- **THEN** the system SHALL remove any in-progress indicator and display the fetched artwork and name, without showing a failed indicator

#### Scenario: Failed indicator with retry
- **WHEN** the fetch for a game fails (no match or error)
- **THEN** the system SHALL show a failed indicator on that game's tile, while still displaying the placeholder artwork, and SHALL offer a way to retry the fetch from that tile

#### Scenario: Retry returns the tile to in-progress
- **WHEN** the user activates the retry action on a failed tile
- **THEN** the system SHALL re-attempt the fetch and return the tile to the in-progress state

#### Scenario: No indicator without an API key
- **WHEN** no SteamGridDB API key is configured
- **THEN** the system SHALL NOT show an in-progress or failed indicator and SHALL display the placeholder as before

#### Scenario: Feedback does not persist across restarts
- **WHEN** the application is restarted
- **THEN** the system SHALL NOT show a failed indicator from a previous session; the feedback reflects only the current session's fetch attempts
