## ADDED Requirements

### Requirement: Edit or remove an individual episode

The series detail view SHALL let the user edit an individual episode's season number, episode number, and title, and SHALL persist those changes. It SHALL also let the user remove an individual episode from the series without deleting the underlying video file on disk. These give the user a way to correct an episode whose number was parsed wrong (e.g. an absolute number that should be within-season) or to remove a mistaken/duplicate episode.

#### Scenario: Edit an episode's season/episode number and title

- **WHEN** the user edits an episode's season number, episode number, or title in the series detail view and saves
- **THEN** the system SHALL persist the new values and re-group/re-order the episode under its season, reflecting the change in the detail view

#### Scenario: Invalid episode number is rejected

- **WHEN** the user saves an episode edit whose season or episode field is not a whole number (0 or greater)
- **THEN** the system SHALL NOT change the stored episode and SHALL inform the user that the value is invalid

#### Scenario: Remove an individual episode

- **WHEN** the user removes a single episode from the series detail view and confirms
- **THEN** the system SHALL delete that episode (and its watch record and cached still) and SHALL NOT delete the underlying video file on disk

#### Scenario: Remove is confirmation-gated

- **WHEN** the user starts removing an episode but declines the confirmation
- **THEN** the system SHALL keep the episode unchanged
