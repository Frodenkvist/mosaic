## Context

Mosaic is a Windows/WPF/.NET 10 app that today manages **non-Steam games**: a `Game` model, a heuristic confirmation-gated folder scan (`GameLibrary.ScanFoldersAsync`), Job-Object-based play tracking (`PlayTracker` + `JobObjectTracker`), SteamGridDB artwork (`ArtworkService` + `SteamGridDbClient`), and a themed library/recently-played/settings shell (`MainWindow` nav rail, `GameCollectionViewModel` tiles). Persistence uses **short-lived** `IDbContextFactory<MosaicDbContext>` contexts (`AsNoTracking()` reads), per-game stats are **derived** from sessions, and background services raise events marshalled to the UI via `App.RunOnUiAsync`. Runtime data lives under `%LOCALAPPDATA%\Mosaic\` (`AppPaths`).

The project was always meant to cover **media** as well as games. The user's media is plain video files in folders (movies, and TV series organised as `Show/Season N/SxxExx`), played by whatever player the OS associates. The goal is to give media a library that is as first-class as the game library, while **reusing the existing patterns rather than the game-specific code** (Game/PlayTracker/ArtworkService are tuned for executables, Job Objects, and SteamGridDB — wrong shape for external video files and a movie/TV metadata source). The existing SteamGridDB integration is the template to clone for the new metadata source: an HTTP client behind an interface, a `SemaphoreSlim` gate, a user-supplied key that gracefully disables auto-fetch when absent, name-similarity matching, cached files under the data dir, and manual overrides never replaced by auto-fetch.

## Goals / Non-Goals

**Goals:**
- A **Media** library parallel to the game library: add media folders, scan them and **all subfolders** for video files, confirmation-gated (never silently add).
- Model **movies** and **TV series → episodes** (seasons grouped within a series) so a season is browsable in order and "where you left off" is answerable.
- **Browse by cover art** in a poster grid; drill into a series' episodes; show watched/progress indicators and a "continue watching" surface.
- **Play with a configured player, or the OS default association** when none is set — no bundled player.
- **Track watch activity**: record opens (for recency), derive each series' **resume point** (next unwatched episode in order), and **automatically detect completion + an in-file resume position** for players that publish playback state to the Windows system media controls — falling back to an explicit manual watched-toggle for players that don't.
- **Auto-fetch posters + light metadata** (incl. per-episode titles/stills) from TMDB by name+year, cached locally, manual override, graceful no-key degradation — mirroring SteamGridDB.
- Reuse the established threading (`App.RunOnUiAsync`), persistence (short-lived contexts, derived stats), and HTTP-client/gate conventions. **No change to any existing game behaviour.**

**Non-Goals:**
- **No bundled/embedded playback engine** and **no in-app player controls** (play/pause/seek). Playback is delegated to the OS default association.
- **No hooking, injecting into, or commanding the external player.** Automatic detection only *reads* the playback state a player voluntarily publishes to Windows' system media controls; we never drive or seek the player. A player that publishes nothing degrades to the manual watched-toggle — manual is the guaranteed floor, auto is the bonus where available.
- **No music, photos, or live-TV/streaming sources** in v1 (video files only). The model leaves room but none ship now.
- **No transcoding, subtitles management, or library sharing/casting.**
- **No changes to the game domain** — Game, PlayTracker/JobObjectTracker, ArtworkService/SteamGridDbClient, and their specs are untouched.

## Decisions

### 1. Media is a parallel domain, not bolted onto `Game`
New media-only models (`MediaItem`, `WatchSession`, `MediaArtwork`) and services (`IMediaLibrary`, `IMediaPlaybackTracker`, `IMediaArtworkService`) sit alongside the game ones, all singletons registered in `App.ConfigureServices`, all using the same short-lived `IDbContextFactory<MosaicDbContext>` pattern.
- Rationale: the game services encode executable/Job-Object/SteamGridDB assumptions that do not fit external video files and a movie/TV metadata API. Sharing the *patterns* (not the classes) keeps each domain clean and leaves the proven game code untouched (a stated goal).
- Alternative considered: generalise `Game` into a polymorphic "library entry." Rejected — it would mutate the game schema, the game specs, and risk the play-tracking logic for no real gain.

### 2. One `MediaItem` table with a kind + self-referential parent; seasons are a grouping attribute
`MediaItem` carries `Kind` (`Movie` | `Series` | `Episode`), a nullable `ParentId` (Episode → Series; Movie/Series are roots), `Title`, `Year` (nullable), `FilePath` (the video file — null for a `Series`, which is a folder grouping), `FolderPath`, `SeasonNumber`/`EpisodeNumber` (nullable; episodes only), `DateAdded`, `WatchedAt` (nullable; explicit watched state), and `TmdbId` (nullable). **Seasons are not entities** — they are the `SeasonNumber` grouping of a series' episodes.
- A series' progress (watched/total episodes) and **resume point** (first episode by `(SeasonNumber, EpisodeNumber)` whose `WatchedAt` is null) are **derived** from its children, never stored denormalised — consistent with deriving play stats from sessions.
- `WatchedAt` is stored on the row (not derived) because completion is **user-asserted** state an external player can't report — the same pattern as `Achievement.UnlockedAt` (explicit, source-owned state persisted directly).
- Rationale: a single self-referential table handles movies and the show→episode hierarchy with the fewest tables, makes "where you left off in a season" a simple ordered query over children, and is extensible (seasons could be promoted to entities later without reshaping movies).
- Alternative considered: separate `Movie`/`Show`/`Season`/`Episode` tables. Rejected — more tables, more joins, and seasons rarely need their own identity.

### 3. Recursive, confirmation-gated scan mirroring the game scan
`MediaLibrary.ScanFoldersAsync` walks each configured media folder **recursively**, collects files with known video extensions (`.mkv`, `.mp4`, `.avi`, `.m4v`, `.mov`, `.wmv`, …), filters junk (sample/trailer/extras/featurette fragments, tiny files), and classifies:
- A file whose path/name matches an episode pattern (`SxxExx`, `1x02`, or a `Season N` ancestor folder) → an **episode** candidate, grouped under a **series** derived from the show folder name.
- Otherwise → a **movie** candidate (a loose file or a movie-folder's main video), titled and year-parsed from the file/folder name.
The result is presented for **user confirmation** before anything is added; already-known files are skipped. Never silently add.
- Rationale: reuses the proven `GameLibrary` scan UX and the project's "confirmation-gated, never silent" principle; the recursive walk satisfies "movies in there and any subfolder."
- Alternative considered: trust a strict Plex/Kodi naming convention. Rejected — real libraries are messy; heuristics + a confirmation step (where the user can drop/relabel candidates) are more forgiving, exactly as the game scan is.

### 4. Playback via a configured player, else the OS default association
`MediaPlaybackTracker.Play(mediaItemId)` opens the item's `FilePath`. If the user has set a **preferred media player** (`PreferredMediaPlayerPath` in settings) and that executable exists, it launches that player with the file as its argument (`ProcessStartInfo { FileName = playerPath, Arguments = "\"<file>\"" }`); otherwise — or if the configured player no longer exists — it falls back to the OS default association (`ProcessStartInfo { FileName = filePath, UseShellExecute = true }`). An empty setting preserves the zero-config default.
- Rationale: the user wants the default player by default but also the option to *always* use a specific player (e.g. always VLC) regardless of file associations; an empty path keeps the no-setup path, a set path gives full control. Either way Mosaic bundles no engine.
- Bonus: forcing a known player also makes **Tier 1 (GSMTC) coverage predictable** — the user can deliberately pick a player that publishes playback state to the system media controls.
- Alternative considered: bundle/automate a specific player (e.g. VLC CLI). Rejected — heavy, opinionated, and unnecessary; pointing at the user's own player exe gives the same control with none of the maintenance.

### 5. Two-tier watch tracking: a reliable manual floor + automatic detection where the player cooperates
We cannot assume anything about the player (we hand the file to the OS association). So watch tracking is layered, and **the manual floor always works** while the automatic tier adds value opportunistically.

**Tier 0 — the floor (always on).** On Play, Mosaic persists a `WatchSession` (`MediaItemId`, `StartedAt`) immediately and raises `WatchStarted` — this drives "recently watched" / "continue watching" ordering. **Watched/finished is settable explicitly** (`SetWatchedAsync`), with a convenience "mark watched & play next" that finishes the current episode and advances the series resume point. This tier has zero external dependencies and is fully unit-testable.

**Tier 1 — automatic detection via the Windows system media controls (GSMTC).** Many modern players (the new Windows Media Player, Movies & TV, browsers, recent VLC) publish their playback state to the OS via the same surface that powers the volume-flyout media widget, readable through `Windows.Media.Control` (`GlobalSystemMediaTransportControlsSessionManager`): playback status (playing/paused/stopped), timeline **position** and **end time**, and media properties (title). After a Play, a `SystemMediaWatchObserver` correlates the media session that appears within a short window of our launch (by timing, corroborated by the published title) to the item we launched, then:
- when reported position passes a **near-the-end threshold (~90% of end time)**, it **auto-marks the item watched** (and, for an episode, makes the next one the series resume target);
- it records the latest position as the item's **`ResumePositionSeconds`**, so "where you left off" can be a real in-file timestamp, not only the next episode. This is **informational** — surfaced as "left off at 23:41" and as a progress bar — because a file-association launch cannot command an arbitrary external player to *seek* to it; in practice most default players (Movies & TV, VLC, MPC) resume from their own remembered position on reopen, and our value drives the watched% decision and the UI.
- The decision itself — *given (position, endTime, prior state), should this be marked watched and what is the resume position?* — is a **pure function**, unit-tested in isolation; only the live session observation is verify-by-running (the same split we used for the achievements `FileSystemWatcher`).

**Graceful fallback.** If no media session can be correlated (the player publishes nothing, e.g. MPC-HC), Tier 1 simply does nothing and the user uses the Tier 0 toggle — mirroring the no-API-key artwork degradation. **Automatic detection only ever *sets* watched / updates the resume position; it never clears a watched state** (clearing is a manual action), so it can't fight the user (the monotonic rule we used for achievement unlocks).

- Rationale: GSMTC is the only way to learn what an *unowned* player is doing without hooking or controlling it, and it yields both real completion and a true resume position. Layering keeps correctness independent of it — the floor is always reliable, the auto tier is upside.
- Alternative considered: track the spawned player **process** like a game (Job Object) and compare against the file's runtime. Rejected — default players are single-instance, so the launched process hands off and exits instantly (the "launcher exits immediately" problem, with no per-title process to fall back to), and "player open for 40 min" ≠ "watched 40 min" (pausing/alt-tab). GSMTC strictly dominates it: actual playback status + position instead of a guess.
- Cost accepted: a target-framework bump to `net10.0-windows10.0.19041.0` to project the WinRT `Windows.Media.Control` namespace (no third-party package), and an observer whose live behaviour is verified by running, not unit tests.

### 6. Metadata + posters from TMDB, cloning the SteamGridDB integration
A new `TmdbClient` (typed `HttpClient`, behind the artwork service) searches movies/TV by title (+year), returns a match with poster/backdrop URLs and overview, and for a series returns its episode list (titles, stills, air dates). `MediaArtworkService` resolves the best match by name+year similarity (reusing the artwork-style scoring), downloads and **caches** images under `%LOCALAPPDATA%\Mosaic\media-artwork\`, and persists `MediaArtwork` rows (path on disk). Calls are serialised by a `SemaphoreSlim` gate so a batch scan isn't rate-limited.
- **Key handling**: a new `TmdbApiKey` in `settings.json`. Absent key → auto-fetch disabled, a clear status, manual override still works — identical to the documented SteamGridDB graceful-degradation path. **Manual overrides are never replaced by auto-fetch.**
- **Episodes**: parsed `(SeasonNumber, EpisodeNumber)` map to the matched series' TMDB episodes to fill episode titles/stills, so cryptic filenames become navigable; unmatched episodes keep their filename-derived title.
- Rationale: TMDB is the standard free movie/TV metadata + image API and maps cleanly onto our existing SteamGridDB machinery (key, gate, caching, matching, override). Cloning a proven pattern beats inventing one.
- Alternative considered: OMDb. Rejected — weaker per-episode/TV image data than TMDB. (Both could coexist behind the client later.)

### 7. Separate `MediaArtwork` table + its own cache dir (don't generalise the game `Artwork`)
`MediaArtwork` (`MediaItemId`, `Kind` = Poster/Backdrop, `LocalPath`, `IsManualOverride`) is a sibling of `Artwork`, cached under `media-artwork\`. `AppPaths` gains `MediaArtworkDirectory` (created in `EnsureCreated`).
- Rationale: leaving the proven game `Artwork` table and its FK to `Game` untouched avoids a risky migration and keeps cascade-delete simple; the duplication is small and the two domains stay decoupled.
- Alternative considered: a polymorphic artwork table referencing either a game or a media item. Rejected — nullable dual FKs and reworking the existing artwork code for no functional gain.

### 8. Same persistence, threading, and cleanup conventions
New entities are added to `MosaicDbContext` with one EF migration generated via `MosaicDbContextFactory` and auto-applied on startup (`Database.MigrateAsync()`). Removing a `MediaItem` cascades to its child episodes, `WatchSession`s, and `MediaArtwork` rows (configured in `OnModelCreating`); the service also deletes the item's cached images, and **only files inside the data dir** — never the user's video files (the existing data-dir rule). Background watch/artwork events are marshalled to the UI via `App.RunOnUiAsync`.
- Rationale: one consistent persistence/threading story across games and media; reuses the documented cascade + data-dir-only cleanup guarantees.

### 9. media-ui reuses the game UI plumbing
A **Media** entry joins the `MainWindow` nav rail (a `ShowMedia` command on `MainViewModel`, selecting a `MediaLibraryViewModel`). Media tiles reuse the `GameCollectionViewModel`-style tile/loading approach (poster + title + watched/progress badge). A `MediaDetailViewModel` shows a movie's details or a series' episodes grouped by season with per-episode watched toggles and the resume/"continue watching" action. All media windows derive from `MosaicWindow`, so the dark theme and chrome are inherited (already covered by the existing library-ui theme requirement).
- Rationale: the tile grid, search/sort, live-badge, and background→UI patterns already exist for games; media gets them by mirroring, not rewriting.

## Risks / Trade-offs

- **The external player may report nothing** (Tier 1 coverage is player-dependent — classic players like MPC-HC don't publish to GSMTC) → the Tier 0 manual toggle is always available and is the guaranteed floor; recency still comes from recorded opens and resume-by-episode still works. Surface which mode applied so the user isn't surprised that auto didn't fire for a given player.
- **Correlating the right media session to the item we launched** (multiple sessions may exist; players reuse one window across files; rapid successive plays) → correlate by the launch we just triggered within a short time window, corroborated by the published title; on any ambiguity, do nothing automatic and leave it to the manual toggle (never auto-mark the wrong item).
- **GSMTC requires the bumped TFM and a minimum Windows build** (`net10.0-windows10.0.19041.0` ⇒ Windows 10 2004+) → that floor is already reasonable for a current WPF app; if the API is unavailable at runtime the observer no-ops and Tier 0 carries the feature.
- **Auto-detection fighting the user** (re-marking watched, flapping) → automatic detection only *sets* watched and updates the resume position, **never clears** a watched state (clearing is manual) — the monotonic rule reused from achievement unlocks.
- **Observer is hard to unit-test** (live system surface) → the watched/resume *decision* is factored into a pure function with unit tests; the live session observation is verified by running the app (same split as the achievements `FileSystemWatcher`).
- **Messy/ambiguous filenames** (no SxxExx, "1x01", date-based, multi-episode files, anime ordering) → a parser handles the common patterns; anything unrecognised falls back to a movie/loose candidate the user can relabel or drop in the confirmation step. Never silently mis-add.
- **TMDB match ambiguity** (remakes, same title different year, regional titles) → match by title **and** parsed year, surface the match, and allow a manual poster override and (where useful) a manual TMDB id — mirroring the artwork "manual override wins" rule.
- **Large libraries / slow recursive scan** → scan runs async off the UI thread and is confirmation-gated with progress; the video-extension + junk filter bounds the candidate set.
- **No TMDB key out of the box** → auto-fetch disabled with a clear status; scanning, playback, and manual marking all work; manual poster override available — identical to the SteamGridDB no-key path. Trade-off: cover art needs the user to obtain a free key.
- **TMDB terms/attribution** → surface the required "data provided by TMDB" attribution in the UI/settings; the key is user-supplied, so we ship no secret.
- **Watch/artwork events racing a disposed media view model** → reuse the existing `App.RunOnUiAsync` + lookup-by-id guarding already used for game session/artwork events; no new exposure.
- **Accidental data loss** → removal deletes only Mosaic-owned cache files under the data dir; the user's video files are never touched (enforced by the same `AppPaths.IsInsideDataDirectory` rule as games).

## Migration Plan

A **target-framework bump** (`net10.0-windows` → `net10.0-windows10.0.19041.0`) lands first so `Windows.Media.Control` is projected; it is binary-compatible and changes no game behaviour, and `installer\package.ps1`'s self-contained `win-x64` publish is unaffected. One additive EF migration (new `MediaItem` — including a nullable `ResumePositionSeconds` — plus `WatchSession` and `MediaArtwork` tables; no changes to existing tables) generated via `MosaicDbContextFactory` and auto-applied on startup; no backfill. New `settings.json` fields (`MediaFolders` empty list, `TmdbApiKey` empty) deserialize to safe defaults so existing installs are unaffected until the user adds a media folder. Rollback is a revert (including the TFM) plus dropping the three new tables; cached media artwork lives only under `%LOCALAPPDATA%\Mosaic\media-artwork\` and is safe to delete.

## Open Questions

- **Completion threshold**: is ~90% of end time the right auto-watched cutoff for everyone, and how should very short clips (where 90% is seconds) be treated? (Make it a constant first; consider exposing it only if users ask.)
- **Session correlation strategy**: how wide a post-launch time window, and how much weight on title-match vs. timing — finalise by observing real players during verification; default to "do nothing automatic on ambiguity."
- **Runtime for a progress bar**: persist a media item's runtime (from GSMTC end time or file metadata) so a partial-progress bar shows before the next play, or recompute live only? (Leaning: store `ResumePositionSeconds`; persist runtime only if the UI needs the bar at rest.)
- **Movie-folder extras**: how aggressively to filter `extras/`, `featurettes/`, `behind the scenes/` and sample files — finalise the junk list from real libraries during implementation (mirrors the game `JunkNameFragments` approach).
- **TMDB vs adding OMDb** as a fallback provider — TMDB only in v1; the client interface leaves room.
- **Episode title source of truth** when TMDB and filename disagree — prefer the TMDB title once matched, keep the filename-derived title otherwise (settle exact precedence in implementation, mirroring artwork's "adopt matched title only when auto-derived").
