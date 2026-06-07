## Why

When a series is filed with **absolute** episode numbering — episodes counted continuously across the whole show rather than restarting each season — Mosaic stores the wrong per-season episode number. A file like `Death Note\Season 2\09 Encounter.mkv` is the 9th episode of the series but the **1st** episode of season 2; Mosaic records it as season 2, episode 9. The metadata match (which looks up season 2, episode 9 on TMDB) then fails, so the episode never gets its real title or still, and it sorts incorrectly. Users with absolute-numbered anime/TV folders hit this on every season after the first.

## What Changes

- During import, Mosaic infers when a series' seasons use absolute (cross-season cumulative) episode numbering and normalizes each episode to a **within-season** number that restarts at 1 per season.
- The inference is conservative: a later season is only renumbered when its first episode number continues directly from the previous season's last episode number (a clear "season 2 starts where season 1 left off" signal). Series that already restart per season are left untouched.
- Normalization considers the series' **already-imported** episodes too, so importing season 2 in a separate pass (after season 1 is already in the library) is corrected, and importing seasons out of order self-corrects once the earlier season arrives.
- The auto-generated `SxxExx` placeholder title of a renumbered episode is regenerated to match its corrected number; a title already supplied by metadata or the user is never overwritten.
- The pre-add confirmation preview reflects the inferred numbering where it can be determined from the scanned batch, so the user confirms the corrected season/episode values.
- The series detail view gains per-episode **Edit** (season/episode number + title) and **Remove** controls, so the user can correct an already-imported episode the inference couldn't fix (e.g. a partially-imported earlier season) or remove a mistaken/duplicate episode.

## Capabilities

### New Capabilities
<!-- None: this refines existing media-library import behavior. -->

### Modified Capabilities
- `media-library`: the episode-classification requirement gains behavior for inferring and correcting absolute cross-season numbering into within-season numbering at import time (including across separate import passes); episode-number determination prefers a leading file-name number over a number embedded in the title.
- `media-ui`: the series detail view gains per-episode edit (season/episode/title) and per-episode remove controls.

## Impact

- **Code**: `Services\MediaLibrary.cs` (`AddConfirmedAsync`, `ScanFoldersAsync`), and a new pure, unit-testable normalization helper (in `MediaNameParser` or a small sibling). No schema/migration change — `MediaItem.SeasonNumber`/`EpisodeNumber` already model within-season numbers.
- **Behavior**: makes the existing TMDB episode match in `Services\MediaArtworkService.cs` (`FillEpisodesAsync`) succeed for absolute-numbered series; that service is not modified.
- **Tests**: new cases in `MediaLibraryTests` (add-time and cross-pass normalization) and `MediaNameParserTests`/a helper test (the pure algorithm).
- **Data**: existing libraries are corrected on the next import that touches the series; no automatic backfill of already-stored items beyond what an import pass re-evaluates.
