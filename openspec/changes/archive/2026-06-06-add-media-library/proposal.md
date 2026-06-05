## Why

Mosaic was always intended to manage **both games and media**, but today it only handles non-Steam games. Users keep their movies and TV shows as plain video files in folders, with no easy way to browse them by cover art, play them, or remember what they have already watched (or which episode of a season they left off on). Extending Mosaic to media reuses everything we already built for games — folder scanning, cover-art fetching, a themed library UI, and local persistence — to deliver a second, equally first-class library.

## What Changes

- Add a **Media** section to the app alongside Library and Recently Played. The user can add one or more **media folders**; Mosaic scans each folder **and all of its subfolders** for video files, with user confirmation before anything is added (mirroring the game-scan flow — never silently add).
- Model media as **movies** and **TV series → episodes** (seasons are a grouping within a series). The scan recognises common layouts (loose movie files, movie folders, `Show/Season N/SxxExx` trees) and groups episodes under their series so a season can be browsed in order.
- **Browse media by cover art.** A new poster-grid view shows every movie and series; opening a series drills into its episodes grouped by season. Each item shows a watched indicator and a series shows its overall progress.
- **Play with a configured or the default media player.** Activating a movie or episode opens the file with a **preferred media player set in Settings**, or — when none is set — the OS file-association (the user's chosen default player). Mosaic does not bundle a player.
- **Track what has been watched.** Mosaic records when an item is opened (for a "recently watched" / "continue watching" ordering) and derives a series' **resume point** — the next unwatched episode in season-and-episode order. Completion is **detected automatically where the player cooperates**: for players that publish playback state to the Windows system media controls, Mosaic observes the position of the item it launched and **auto-marks it watched** near the end (~90%) and remembers an **in-file resume position** ("resume at 23:41"); for players that publish nothing, the user marks an item watched manually. Either way the user can tell where they left off.
- **Auto-fetch posters and metadata** for movies and series from a movie/TV metadata provider (TMDB) when a user-supplied API key is configured, cached locally, with graceful degradation when no key is set and a manual poster override — exactly as the existing SteamGridDB artwork integration behaves. Series additionally get episode titles/stills so cryptic filenames become navigable.

This change adds media-only models, services, and views and **does not alter any existing game behaviour** (Game, PlayTracker/Job-Object tracking, and the SteamGridDB ArtworkService are untouched). Its one cross-cutting change is a **target-framework bump** (`net10.0-windows` → `net10.0-windows10.0.19041.0`) needed to reach the Windows media-controls API; this is binary-compatible and changes no game behaviour.

## Capabilities

### New Capabilities
- `media-library`: configuring media folders, recursively scanning them for video files (confirmation-gated), modelling movies and TV series/episodes with their season/episode structure, and persisting, editing, and removing media items.
- `media-playback`: opening a movie or episode with the system default media player, recording watch activity, marking items watched/finished, and deriving watch statistics and a series' resume point ("where you left off").
- `media-artwork`: auto-fetching posters/backdrops and descriptive metadata (incl. per-episode titles/stills) for media from TMDB by name+year match, caching them locally, manual override, and graceful degradation without an API key.
- `media-ui`: a Media navigation section and poster-grid browsing view, drill-in to a series' seasons/episodes, watched/progress indicators, a "continue watching" surface, and the play action — reusing the dark theme and background-to-UI event plumbing.

### Modified Capabilities
<!-- None — the change is additive; no existing requirements change. -->

## Impact

- **Models**: new `MediaItem` (kind Movie/Series/Episode, self-referential `ParentId`, file/folder path, title, year, season/episode numbers, watched state, **resume position**, TMDB id), `WatchSession` (started/ended, for history & ordering), and `MediaArtwork` (poster/backdrop, local path, manual-override flag). EF Core migration auto-applied on startup.
- **Services**: new `IMediaLibrary`/`MediaLibrary` (folder scan + CRUD), `IMediaPlaybackTracker`/`MediaPlaybackTracker` (shell-execute launch + watch tracking), a `SystemMediaWatchObserver` (reads `Windows.Media.Control` to auto-detect completion + resume position, with manual fallback), `IMediaArtworkService`/`MediaArtworkService` + a `TmdbClient` (sibling of `SteamGridDbClient`, throttled by a `SemaphoreSlim`, graceful no-key behaviour). Short-lived `IDbContextFactory<MosaicDbContext>` per operation; no shared context. No changes to `GameLibrary`, `PlayTracker`, or `ArtworkService`.
- **Settings**: new `MediaFolders` list, `TmdbApiKey`, and an optional `PreferredMediaPlayerPath` in `AppSettings`/`settings.json` and the Settings view (alongside scan folders and the SteamGridDB key); all default empty, and an empty player path means "use the OS default association".
- **ViewModels/Views**: new media view models (`MediaLibraryViewModel`, media tile + `MediaDetailViewModel`) and views (a media grid view, a media detail/episodes window), plus a **Media** entry in `MainWindow`'s navigation rail; artwork/watch events marshalled to the UI via `App.RunOnUiAsync`, as for games.
- **Data dir**: cached media artwork under a new `%LOCALAPPDATA%\Mosaic\media-artwork\` directory (`AppPaths`), consistent with `artwork\`. Removing a media item deletes its cached images only — never the user's video files.
- **Tests**: `Mosaic.Tests` — scan/grouping of movie & series layouts and SxxExx parsing, watched-state + series resume-point derivation, TMDB match + caching + no-key degradation + manual override, and cascade-delete cleanup on item removal.
- **Dependencies**: TMDB HTTP API (user-supplied free key); no new runtime engine — playback delegates to the OS default association. The Windows media-controls API (`Windows.Media.Control`) comes with the bumped Windows target framework — no third-party package. TMDB attribution to be surfaced per their terms.
