## 1. Project setup, data model & migration

- [x] 1.0 **Prerequisite (do first):** bump the target framework `net10.0-windows` → `net10.0-windows10.0.19041.0` in `Mosaic.csproj` so the WinRT `Windows.Media.Control` namespace is projected (no third-party package). Confirm `dotnet build`/`dotnet run` and `installer\package.ps1` (self-contained `win-x64`) still succeed; no game behaviour changes.
- [x] 1.1 Add `MediaItem` model under `Models\` — `Id`, `Kind` (enum `MediaKind` Movie/Series/Episode), `ParentId` (nullable self-FK), `Title`, `Year` (nullable), `FilePath` (nullable; null for a Series), `FolderPath`, `SeasonNumber`/`EpisodeNumber` (nullable), `DateAdded`, `WatchedAt` (nullable DateTimeOffset; explicit watched state), `ResumePositionSeconds` (nullable; in-file position from Tier 1), `TmdbId` (nullable), and `Episodes`/`Artwork`/`WatchSessions` navigation collections.
- [x] 1.2 Add `WatchSession` model — `Id`, `MediaItemId`, `StartedAt`, `EndedAt` (nullable).
- [x] 1.3 Add `MediaArtwork` model — `Id`, `MediaItemId`, `Kind` (enum Poster/Backdrop/EpisodeStill), `LocalPath`, `SourceId` (nullable), `IsManualOverride`.
- [x] 1.4 Register `DbSet`s in `MosaicDbContext` and configure in `OnModelCreating`: self-referential `MediaItem` parent→children, indexes (`ParentId`, `FilePath` unique-where-not-null, `(MediaItemId, Kind)` on artwork, `MediaItemId`+`EndedAt` on sessions), and cascade-delete of a media item's episodes, watch sessions, and artwork (mirror the Game cascades). Do **not** touch existing Game/Artwork/PlaySession config.
- [x] 1.5 Add `MediaArtworkDirectory` to `AppPaths` (`%LOCALAPPDATA%\Mosaic\media-artwork\`) and create it in `EnsureCreated`.
- [x] 1.6 Generate the EF migration via `MosaicDbContextFactory` (e.g. `AddMediaLibrary`); the app auto-applies it on startup via the existing `Database.MigrateAsync()`.

## 2. Settings

- [x] 2.1 Add `MediaFolders` (`List<string>`, empty default), `TmdbApiKey` (`string?`), and `PreferredMediaPlayerPath` (`string?`, empty = use OS default association) to `AppSettings`; confirm old `settings.json` deserializes to safe defaults.
- [x] 2.2 Surface media folders (add/remove list), the TMDB API key (password box + show toggle), and a preferred media player picker (browse to a player `.exe`, with a Clear/"use system default" option) in `SettingsView`/`SettingsViewModel`, alongside scan folders and the SteamGridDB key; note that an absent key disables auto-fetch (manual override still works) and show the required TMDB attribution.

## 3. Filename parsing & scan

- [x] 3.1 Add a media filename parser (e.g. `MediaNameParser`) that detects episode patterns (`SxxExx`, `1x02`, `Season N` ancestor folder) yielding `(showName, season, episode)`, and parses a movie title + year from a file/folder name. Unit-testable, no I/O.
- [x] 3.2 Define video extensions and junk filters (sample/trailer/extras/featurette fragments, trivial-size cutoff) mirroring `GameLibrary`'s `JunkNameFragments` approach.
- [x] 3.3 Implement `IMediaLibrary.ScanFoldersAsync` — recursively enumerate video files under each configured media folder, filter junk, skip files already in the library, classify into movie vs. series/episode candidates grouped by show, and return candidates (a `MediaScanCandidate` record) for confirmation. Never add without confirmation.

## 4. Media library service (CRUD & persistence)

- [x] 4.1 Add `IMediaLibrary` / `MediaLibrary` singleton using short-lived `IDbContextFactory<MosaicDbContext>` contexts (`AsNoTracking()` reads); register in `App.ConfigureServices`.
- [x] 4.2 `AddConfirmedAsync(candidates)` — persist confirmed movies, and create-or-reuse a Series row then attach episodes with parsed season/episode numbers; trigger artwork fetch per added item.
- [x] 4.3 Query methods: top-level media (movies + series) with derived progress and poster path (a `MediaListItem` record), and a series' episodes grouped/ordered by season then episode.
- [x] 4.4 `EditAsync` (title, year, classification/season/episode) and `RemoveAsync` (cascade episodes/sessions/artwork; delete cached images via `AppPaths.IsInsideDataDirectory`; never delete the user's video files).

## 5. Playback & watch tracking service (Tier 0 — the reliable floor)

- [x] 5.1 Add `IMediaPlaybackTracker` / `MediaPlaybackTracker` singleton; register in `App.ConfigureServices`.
- [x] 5.2 `PlayAsync(mediaItemId)` — verify the file exists (else report error, record nothing); resolve the preferred player to null when it is unset or its exe is missing (the only I/O check), then build the launch via a pure helper `ResolveLaunch(filePath, preferredPlayerPath)` → `ProcessStartInfo` (non-null player ⇒ `FileName = playerPath`, `Arguments = "\"<file>\""`; null ⇒ `FileName = filePath`, `UseShellExecute = true`), start it, then persist an open `WatchSession` and raise `WatchStarted`.
- [x] 5.3 `SetWatchedAsync(mediaItemId, watched)` — persist `WatchedAt` (set/clear); raise `WatchStateChanged`. (Manual is the only path that may *clear* watched.)
- [x] 5.4 `MarkWatchedAndAdvance(episodeId)` — mark watched and return the series' next unwatched episode as the resume target.
- [x] 5.5 Derived queries: series progress (watched/total) and resume point (first episode by `(SeasonNumber, EpisodeNumber)` with null `WatchedAt`); recently-watched ordering from `WatchSession`s.
- [x] 5.6 Lifecycle events `WatchStarted` / `WatchStateChanged` raised on background threads (for `App.RunOnUiAsync` marshalling).

## 5b. Automatic watch detection (Tier 1 — GSMTC, graceful)

- [x] 5b.1 Factor a **pure decision function** `WatchProgress.Evaluate(positionSeconds, endTimeSeconds, alreadyWatched)` → `(bool MarkWatched, double? ResumePositionSeconds)`: mark watched at ≥ ~90% of end time; otherwise return the resume position; never return `MarkWatched=false`-clears (only sets). Unit-testable, no I/O.
- [x] 5b.2 Add `SystemMediaWatchObserver` reading `Windows.Media.Control` (`GlobalSystemMediaTransportControlsSessionManager`): after a `WatchStarted`, correlate the session appearing within a short window of the launch (corroborated by published title) to the launched item; on ambiguity, make no automatic change.
- [x] 5b.3 On correlated playback updates, call the §5b.1 decision function and apply via `MediaPlaybackTracker` — auto-set `WatchedAt` (and advance the series resume target for an episode), and persist `ResumePositionSeconds`; reuse the §5.6 events for the UI.
- [x] 5b.4 Graceful fallback: if the API is unavailable at runtime or no session correlates, no-op silently (Tier 0 carries the feature); expose which mode applied so the UI can reflect it.

## 6. TMDB client & media artwork service

- [x] 6.1 Add `TmdbClient` (typed `HttpClient`) — search movies/TV by title (+year), get poster/backdrop URLs + overview, and get a series' episode list (title, still, air date). Register the `HttpClient` in `App.ConfigureServices`.
- [x] 6.2 Add `IMediaArtworkService` / `MediaArtworkService` singleton with a `SemaphoreSlim` gate (serialize TMDB calls so batch scans aren't rate-limited); cheap no-key guard returns immediately.
- [x] 6.3 Match resolution by title + parsed year using name-similarity scoring (reuse/adapt `ArtworkService.Similarity`/term-cleaning); accept only above a threshold.
- [x] 6.4 Download + cache poster/backdrop under `MediaArtworkDirectory`; upsert `MediaArtwork` rows (paths persisted, bytes on disk); reuse cached files; never replace a manual override.
- [x] 6.5 For a matched series, fetch episode metadata and fill episode title/still by `(season, episode)`; unmatched episodes keep their filename-derived title.
- [x] 6.6 `SetManualOverrideAsync` (local image → poster, manual flag); `FetchMissingForAllAsync` (skip items with art or override); raise `MediaArtworkUpdated`/`...FetchStarted`/`...FetchFailed` on background threads. Graceful no-key degradation throughout.

## 7. ViewModels

- [x] 7.1 Add a `MediaTileViewModel` (poster path, title, watched indicator, series progress) and a media collection view model mirroring `GameCollectionViewModel`'s tile-loading/search/sort.
- [x] 7.2 Add `MediaLibraryViewModel` — loads top-level media, exposes play/open/remove commands, a continue-watching/recently-watched surface ordered by watch activity, and subscribes to `MediaArtworkUpdated`/`WatchStateChanged` via `App.RunOnUiAsync` to update tiles by id.
- [x] 7.3 Add `MediaDetailViewModel` — movie details (metadata, play, watched toggle, manual poster override) and series details (episodes grouped by season, per-episode watched toggle, resume/"continue watching" action).
- [x] 7.4 Add `ShowMedia` command + `Media` section to `MainViewModel` (parallel to `ShowLibrary`/`ShowRecentlyPlayed`), wiring the `MediaLibraryViewModel`.

## 8. Views (XAML)

- [x] 8.1 Add a **Media** entry to `MainWindow.xaml`'s navigation rail (mirroring the Library/Recently Played buttons, with the `SectionActive` converter binding).
- [x] 8.2 Add a media library view (poster grid) — posters/placeholder, titles, watched badge, series progress, empty-state guidance prompting to add a media folder; derive from existing tile styling.
- [x] 8.3 Add a media detail window/view — movie details and a series' season-grouped episode list with watched toggles, play, resume, and manual-poster controls, plus a "left off at HH:MM" / partial-progress indicator where a `ResumePositionSeconds` is known; derive from `MosaicWindow` for the dark theme/chrome.
- [x] 8.4 Add a scan-results confirmation window for media candidates (mirroring `ScanResultsWindow`), allowing the user to confirm/drop/relabel before adding.

## 9. Tests

- [x] 9.1 `MediaNameParserTests` — `SxxExx`/`1x02`/`Season N`-folder episode parsing and movie title+year parsing; ambiguous names fall back to a movie candidate.
- [x] 9.2 Scan tests — recursive enumeration across subfolders, junk/sample exclusion, skip-already-known, and grouping episodes under a series with correct season/episode numbers.
- [x] 9.3 Watch-tracking tests — `WatchedAt` set/clear, recently-watched ordering from sessions, series progress derivation, resume point = next unwatched episode, fully-watched series returns no resume, watch-and-advance.
- [x] 9.3b `WatchProgress.Evaluate` tests (the §5b.1 pure function) — auto-watched at/above the threshold, resume position returned below it, never clears an already-watched item, and short-clip edge handling. (The live `SystemMediaWatchObserver` correlation is timing-dependent and left to manual verification in §10.)
- [x] 9.3c `ResolveLaunch` tests (the §5.2 helper) — a non-null player path ⇒ launches that player with the file quoted as its argument; a null player path ⇒ default association on the file. (PlayAsync passes null when the configured player is unset or its exe is missing, so both the "unset" and "missing" cases resolve to the default.)
- [x] 9.4 Media artwork tests — title+year match + caching on disk, episode metadata fill by `(season, episode)`, manual override preserved across refetch, and no-key degradation (no HTTP; manual override still works).
- [x] 9.5 Removal tests — removing a series cascades episodes/sessions/artwork and deletes cached images, and never deletes the underlying video files (assert files on disk untouched).

## 10. Verification

- [x] 10.1 `dotnet build Mosaic.sln` and `dotnet test` green (0 warnings); confirm existing game tests still pass (no regressions to the game domain).
- [x] 10.2 Verified by running the built app: it launches cleanly (so `Database.MigrateAsync()` applied the new migration to the real DB and DI resolved all media services + the `SystemMediaWatchObserver` with no startup crash), and the new **🎬 Media** entry renders in the themed nav rail between Recently Played and Settings (screenshot captured). Separately confirmed `dotnet ef database update` applies `20260604135349_AddMediaLibrary` to a fresh SQLite DB (filtered unique index + self-ref cascade are valid SQL). The remaining interactive flows are left for the user to exercise on their real library/key, as scoped: scan→confirm→grid, series→episodes drill-in, playback via the default/configured player, watched toggles + resume, live TMDB posters/metadata, and the Tier-1 GSMTC auto-watch (cooperating player auto-marks at ~90% and records a "left off at" position; a non-publishing player silently falls back to the manual toggle). That logic is covered by the 28 new unit tests (parse, scan/grouping, watched/resume derivation, `WatchProgress.Evaluate`, `ResolveLaunch`, TMDB match+cache+episode-fill+manual-override+no-key, and cascade-delete cleanup).
