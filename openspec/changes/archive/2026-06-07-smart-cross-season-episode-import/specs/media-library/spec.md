## ADDED Requirements

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
