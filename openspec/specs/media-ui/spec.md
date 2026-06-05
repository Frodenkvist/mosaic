# media-ui Specification

## Purpose
TBD - created by archiving change add-media-library. Update Purpose after archive.
## Requirements
### Requirement: Media navigation section
The system SHALL provide a Media section in the application's primary navigation that opens the media library view, consistent with the existing navigation entries and the dark theme.

#### Scenario: Open the media section
- **WHEN** the user selects the Media entry in the primary navigation
- **THEN** the system SHALL display the media library view and indicate Media as the active section

### Requirement: Display the media library
The system SHALL display the user's media in a browsable poster grid showing each movie's and series' artwork (or a placeholder) and title.

#### Scenario: Media library shows all top-level media
- **WHEN** the user opens the media library view
- **THEN** the system SHALL display every movie and series with its poster (or a placeholder) and title

#### Scenario: Empty media library guidance
- **WHEN** no media folders are configured or no media has been added
- **THEN** the system SHALL display guidance prompting the user to add a media folder and scan it

### Requirement: Browse a series' seasons and episodes
The system SHALL provide a detail view for a series that lists its episodes grouped and ordered by season and episode, each showing its watched state, and SHALL provide a detail view for a movie showing its metadata and play action.

#### Scenario: View a series' episodes
- **WHEN** the user opens a series
- **THEN** the system SHALL list the series' episodes grouped by season and ordered by episode, each with its title (from metadata when available) and watched state

#### Scenario: View a movie's details
- **WHEN** the user opens a movie
- **THEN** the system SHALL display the movie's title, year, poster, watched state, and a play action

### Requirement: Watched and progress indicators
The system SHALL indicate, in the media views, which items are watched and SHALL show a series' overall watched/total episode progress.

#### Scenario: Watched indicator on items
- **WHEN** a movie or episode is marked watched
- **THEN** the system SHALL show a watched indicator on that item

#### Scenario: Series progress indicator
- **WHEN** a series tile or detail view is displayed
- **THEN** the system SHALL show the series' watched-out-of-total episode progress, updating as episodes are marked watched

### Requirement: Continue watching and resume
The system SHALL surface a way to resume a series at its next unwatched episode, SHALL order a "continue watching" / recently-watched surface by most recent watch activity, and SHALL display a recorded in-file resume position for a partially-watched item where one is known.

#### Scenario: Resume a series
- **WHEN** the user activates the resume action on a series that has an unwatched episode
- **THEN** the system SHALL play the series' next unwatched episode (ordered by season then episode)

#### Scenario: Continue-watching ordering
- **WHEN** the user views the continue-watching / recently-watched surface
- **THEN** the system SHALL list media ordered by most recent watch activity

#### Scenario: Show where the user left off
- **WHEN** an item has a recorded in-file resume position and is not yet watched
- **THEN** the system SHALL indicate where the user left off (e.g. a "left off at" time and/or a partial-progress indicator)

### Requirement: Play and watched actions from the UI
The system SHALL provide, from the media views, a play action that opens an item in the default media player and controls to mark an item watched or unwatched.

#### Scenario: Play from the media UI
- **WHEN** the user activates the play action on a movie or episode
- **THEN** the system SHALL open it in the default media player and record the watch activity

#### Scenario: Toggle watched from the media UI
- **WHEN** the user toggles a movie's or episode's watched state from the media UI
- **THEN** the system SHALL persist the change and update the watched and progress indicators
