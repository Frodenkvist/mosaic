# media-library Specification

## Purpose
TBD - created by archiving change add-media-library. Update Purpose after archive.
## Requirements
### Requirement: Configure media folders
The system SHALL allow the user to configure one or more media folders that Mosaic scans for video files, and SHALL persist them so they are restored on restart.

#### Scenario: Add a media folder
- **WHEN** the user adds a folder as a media folder
- **THEN** the system SHALL persist that folder in the media-folder configuration and make it available to scan

#### Scenario: Remove a media folder
- **WHEN** the user removes a configured media folder
- **THEN** the system SHALL stop scanning that folder and SHALL NOT remove any media items the user already confirmed from it unless the user removes those items

#### Scenario: Media folders restored on restart
- **WHEN** the application restarts
- **THEN** the system SHALL load the previously configured media folders

### Requirement: Scan media folders recursively for video files
The system SHALL scan each configured media folder and **all of its subfolders** for video files, SHALL exclude non-content files (e.g. samples, trailers, extras/featurettes, and files below a trivial size), and SHALL skip files already present in the media library.

#### Scenario: Recursive enumeration of video files
- **WHEN** the user scans a configured media folder
- **THEN** the system SHALL enumerate video files in that folder and every subfolder beneath it and present the resulting candidates

#### Scenario: Exclude non-content files
- **WHEN** scanning encounters files such as sample clips, trailers, extras/featurettes, or files below a trivial size threshold
- **THEN** the system SHALL exclude them from the candidate list

#### Scenario: Skip files already in the library
- **WHEN** a scanned video file's path already exists in the media library
- **THEN** the system SHALL exclude it from the candidate list

### Requirement: Confirmation-gated addition of scanned media
The system SHALL NOT add any scanned media item without explicit user confirmation.

#### Scenario: Candidates presented for confirmation
- **WHEN** a media scan completes
- **THEN** the system SHALL present the candidate movies and series/episodes to the user and SHALL add only the candidates the user explicitly confirms

#### Scenario: Nothing added without confirmation
- **WHEN** the user cancels or dismisses the scan results without confirming
- **THEN** the system SHALL NOT add any media item to the library

### Requirement: Classify movies and TV series with their episodes
The system SHALL classify each confirmed video file as either a movie or an episode of a TV series, SHALL group episodes under a series derived from their show folder, and SHALL associate each episode with a season number and an episode number when these can be determined from the file or folder naming.

#### Scenario: Loose or single video classified as a movie
- **WHEN** a confirmed video file does not match an episode naming pattern and is not under a recognized season/show structure
- **THEN** the system SHALL create a movie media item titled from the file or its containing folder, with a year when one can be parsed

#### Scenario: Episode files grouped under a series
- **WHEN** confirmed video files match an episode pattern (e.g. `SxxExx`, `1x02`) or live under a `Season N` folder within a show folder
- **THEN** the system SHALL create or reuse a series media item for that show and attach each file as an episode with its parsed season and episode numbers

#### Scenario: Season grouping within a series
- **WHEN** a series has episodes spanning multiple seasons
- **THEN** the system SHALL retain each episode's season and episode number so the series' episodes can be presented grouped and ordered by season and episode

#### Scenario: Unrecognized episode numbering falls back to a confirmable candidate
- **WHEN** a video file cannot be confidently parsed as an episode
- **THEN** the system SHALL present it as a movie/loose candidate the user can confirm, relabel, or drop, rather than silently misclassifying it

### Requirement: Edit a media item
The system SHALL allow the user to edit a media item's editable metadata, including its title, year, and its classification (movie vs. series episode, including season/episode numbers), and SHALL persist the changes.

#### Scenario: Edit media metadata
- **WHEN** the user edits a media item's title, year, or season/episode numbers
- **THEN** the system SHALL persist the updated values and reflect them in the media library

### Requirement: Remove a media item
The system SHALL allow the user to remove a media item from the library, SHALL cascade the removal to a series' episodes, watch records, and cached artwork, and SHALL NOT delete the user's video files on disk.

#### Scenario: Remove a movie or series
- **WHEN** the user removes a media item
- **THEN** the system SHALL delete that item (and, for a series, its episodes), along with its watch records and cached artwork

#### Scenario: Removal does not delete the user's files
- **WHEN** the user removes a media item
- **THEN** the system SHALL NOT delete the underlying video files or any files outside Mosaic's own data directory

### Requirement: Persist the media library
The system SHALL persist all media items and their metadata to local storage so the media library is restored on application restart.

#### Scenario: Media library restored on restart
- **WHEN** the application is restarted
- **THEN** the system SHALL load all previously added media items, including the series/episode hierarchy, from local storage

### Requirement: Infer and correct absolute cross-season episode numbering

The system SHALL detect when a series' episodes are numbered **absolutely** (counted continuously across seasons) and SHALL normalize each episode to a **within-season** episode number that restarts at 1 for each season, so the stored season/episode numbers match the canonical per-season numbering used for metadata matching, ordering, and display. The system SHALL make this inference conservatively, treating a later season as absolute-numbered only when its lowest episode number continues directly from the previous season's highest episode number, and SHALL leave a series that already restarts its episode numbering each season unchanged. The inference SHALL consider the series' already-imported episodes together with the episodes being added, so that it holds across separate import passes and regardless of the order in which seasons are imported.

#### Scenario: Absolute numbering across seasons normalized within one import

- **WHEN** a confirmed batch adds a series whose season 1 holds episodes numbered 1..N and a later season's first file continues at N+1 (e.g. `Death Note\Season 1\1 Rebirth.mkv` … `Death Note\Season 1\8 ….mkv` and `Death Note\Season 2\09 Encounter.mkv` …)
- **THEN** the system SHALL store the later season's episodes with within-season numbers restarting at 1 (the `Season 2\09` file becomes season 2, episode 1), while season 1's episodes are unchanged

#### Scenario: Correction across a separate, later import pass

- **WHEN** a series' season 1 (episodes 1..N) is already in the library and the user later imports that series' next season whose files continue the absolute count at N+1
- **THEN** the system SHALL renumber the newly added season to start at episode 1, using the already-stored earlier-season episodes to determine the continuation boundary

#### Scenario: Out-of-order import self-corrects when the earlier season arrives

- **WHEN** a later season is imported before its earlier season exists in the library (so the continuation boundary cannot yet be determined and the later season is stored with its absolute numbers)
- **AND** the user subsequently imports the earlier season that establishes the boundary
- **THEN** the system SHALL re-evaluate the series and update the previously-stored later-season episodes to within-season numbers restarting at 1

#### Scenario: Series that already restarts per season is left unchanged

- **WHEN** a series' episodes already restart at episode 1 for each season (standard within-season numbering)
- **THEN** the system SHALL NOT alter any episode's season or episode number, and re-running an import on the same series SHALL be idempotent

#### Scenario: Only the auto-generated placeholder title is regenerated

- **WHEN** normalization changes an episode's number and that episode still carries its auto-generated `SxxExx` placeholder title
- **THEN** the system SHALL regenerate the placeholder to match the corrected number
- **AND WHEN** the episode already carries a title supplied by metadata or edited by the user
- **THEN** the system SHALL preserve that title and change only the episode number

#### Scenario: Confirmation preview reflects the inferred numbering

- **WHEN** a scan produces episode candidates for a series whose numbering can be inferred as absolute from the scanned files and any already-stored episodes of that series
- **THEN** the system SHALL present the candidates in the confirmation step with their corrected within-season season/episode numbers, so the user confirms the values that will be stored

### Requirement: Prefer a leading file-name number as the episode number

When determining an episode number for a file under a recognized `Season N` folder that carries no explicit episode marker (no `SxxExx`, `NxNN`, `E##`/`Episode ##`, or dash/`#`-delimited number), the system SHALL treat a number at the **start** of the file name (the `<episode> <title>` convention) as the episode number, in preference to a number that appears later within the title.

#### Scenario: Leading number wins over a number embedded in the title

- **WHEN** an episode file is named like `36 1.28 (January 28).mkv` under a `Season 1` folder (a leading episode number followed by a title that itself contains numbers)
- **THEN** the system SHALL parse the episode as number 36 (the leading number), not 28 (a number inside the title)

#### Scenario: Leading number with a simple title

- **WHEN** an episode file is named like `1 Rebirth.mkv` under a `Season N` folder
- **THEN** the system SHALL parse its episode number as the leading number (1)

