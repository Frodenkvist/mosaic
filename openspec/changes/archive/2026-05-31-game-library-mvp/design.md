## Context

Mosaic is a greenfield Windows desktop app (.NET 10, WPF). The goal of this change is a usable MVP: a library of non-Steam games with accurate, automatic play-time tracking and auto-fetched artwork.

The hard problem is **accurate play-time tracking**. Naively, "launch the exe with `Process.Start`, wait for exit, record the elapsed time" fails for a large fraction of real games: many ship a thin launcher that spawns the actual game as a separate process and then exits immediately. Watching only the process you started records a 3-second session for a 2-hour play session. Getting this right is the core of the product's value, so the design centers on it and it is built/validated first.

Everything else (persistence, artwork, UI) is comparatively conventional and exists to surface the tracked data.

## Goals / Non-Goals

**Goals:**
- Accurate play-time tracking that survives launcher-spawns-game and child-process trees.
- Add games manually (pick `.exe`) and by scanning user-configured folders.
- Persist library, metadata, and per-session play history locally.
- Auto-fetch cover/grid/logo art from SteamGridDB, cached on disk, with manual override.
- A clean MVVM WPF UI: library grid, game detail, recently-played.

**Non-Goals:**
- Cross-platform support (Job Objects + WPF are Windows-only).
- Importing from third-party launchers (Epic/GOG/Xbox manifests) — deferred to a later change.
- Cloud sync, multi-user, or sharing.
- Steam integration (these are explicitly the non-Steam games).
- Achievements, mods, save management, controller config.

## Decisions

### 1. Process tracking via Windows Job Objects (not `Process.WaitForExit`)
When launching a game, create a **Job Object**, assign the launched process to it, and use `JOBOBJECT_ASSOCIATE_COMPLETION_PORT` (an IO completion port) to receive a notification when the **last** process in the job exits. Child/descendant processes are automatically assigned to the job, so the session ends only when the entire process tree is gone.

- **Why**: Robust against the launcher-exits-early pattern; no polling; OS guarantees we see the whole tree.
- **Per-game override**: For games launched via a separate store/launcher process we don't own (the job can't capture an already-running unrelated process), allow a per-game **"real executable"** setting. When set, Mosaic also/instead polls for that executable by name and times the session on its presence.
- **Alternatives considered**: (a) `Process.WaitForExit` on the launched process — rejected, breaks on launchers. (b) Polling all running processes by name only — works but racy and CPU-heavier; kept as the fallback for the override case. (c) WMI process-creation events — heavier and higher-latency than Job Objects.
- This is implemented as an `IPlayTracker` service and is built and tested first, before any UI.

### 2. Local persistence via SQLite, accessed through EF Core
Store the library, metadata, and play sessions in a single SQLite file under `%LOCALAPPDATA%\Mosaic\`, accessed via **EF Core** (`Microsoft.EntityFrameworkCore.Sqlite`).

- **Why SQLite**: Play sessions are append-heavy time-series-ish data and stats are aggregate queries (SUM playtime, MAX last-played) — relational fits better than a JSON blob that must be fully rewritten. SQLite is embedded, zero-config, single-file, easy to back up.
- **Why EF Core over raw `Microsoft.Data.Sqlite` + Dapper**: This is an MVP whose schema will evolve across future changes (tags, favorites, genres), so built-in **migrations** are worth more than raw-SQLite's lighter footprint. EF Core also models the **cascade delete** requirement (removing a game deletes its sessions/artwork) declaratively via navigation properties, and removes per-query SQL/row-mapping boilerplate. For a single-user desktop app the query volume never reaches the point where raw SQLite's performance edge matters.
- **Cost accepted**: EF Core adds startup latency (model build + provider warm-up) and a heavier dependency. Mitigate by lazy-initializing the `DbContext` after the main window shows and, if needed, using compiled models. Use short-lived `DbContext` instances (per operation) rather than one long-lived context, to avoid change-tracking surprises in a long-running WPF process; the play-tracker's session writes and UI reads each use their own context with WAL mode enabled.
- **Schema (initial)**: `Games(Id, Name, ExecutablePath, RealExecutableName?, LaunchArguments?, WorkingDirectory?, DateAdded)`, `PlaySessions(Id, GameId, StartedAt, EndedAt, DurationSeconds)`, `Artwork(GameId, Kind, LocalPath, SourceId)`. Per-game totals and last-played are derived from `PlaySessions` (not stored denormalized). Schema evolution is handled by EF Core migrations.
- **Alternatives**: (a) Raw `Microsoft.Data.Sqlite` + Dapper — faster startup, smaller footprint, full SQL control, but requires a hand-written `PRAGMA user_version` migration runner and manual relationship/cascade handling; rejected for the MVP since schema churn and cascade semantics dominate. (b) JSON file store — simpler but poor for aggregation and concurrent writes from the tracker; rejected.

### 3. MVVM with CommunityToolkit.Mvvm
Use `CommunityToolkit.Mvvm` (source-generated `[ObservableProperty]` / `[RelayCommand]`) for viewmodels; views are XAML bound to viewmodels. Services (`IGameLibrary`, `IPlayTracker`, `IArtworkService`) are injected via a simple DI container (`Microsoft.Extensions.DependencyInstection`/Hosting).

- **Why**: Standard, low-boilerplate WPF architecture; testable services decoupled from XAML.
- **Alternatives**: Hand-rolled `INotifyPropertyChanged` — more boilerplate; a heavier framework (Prism) — overkill for an MVP.

### 4. SteamGridDB for artwork, fetched async and cached on disk
`IArtworkService` calls the SteamGridDB HTTP API to resolve a game by name and download grid/hero/logo images into `%LOCALAPPDATA%\Mosaic\artwork\`. The DB stores only local paths. Fetch is best-effort and asynchronous so the UI never blocks; failures leave a placeholder.

- **Why**: SteamGridDB has a public API purpose-built for non-Steam cover art. Caching avoids re-downloading and respects rate limits.
- **API key**: Read from user settings; if absent, artwork features are disabled gracefully (manual artwork still works).
- **Manual override**: User can set a local image file for any artwork slot; overrides are never replaced by auto-fetch.

### 5. Folder scanning is heuristic and confirmation-gated
Scanning a folder enumerates candidate executables (filtering out obvious non-games: uninstallers, crash handlers, redistributables like `vcredist`, `UnityCrashHandler`, etc.) and presents candidates for the user to confirm before they're added. Scan never silently adds entries.

- **Why**: Auto-detection is inherently imperfect; a confirmation step prevents library pollution while still saving manual typing.

## Risks / Trade-offs

- **Launcher we don't own (e.g. game requires its own store client running)** → Job Object can't capture a pre-existing unrelated process. Mitigation: per-game "real executable" override with name-based polling fallback.
- **Job Object interop bugs / handle leaks (P/Invoke)** → wrong or zero play time, leaked handles. Mitigation: isolate all interop in one well-tested `JobObjectTracker` class with `SafeHandle`s; cover the launcher-exits-early case with an integration test using a stub launcher.
- **App crash mid-session loses the open session** → play time undercounted. Mitigation: write a session `StartedAt` row immediately on launch; on next startup, reconcile any session with no `EndedAt` (close it at last-known or discard per policy) rather than losing the row.
- **SteamGridDB rate limits / wrong match** → throttling or incorrect art. Mitigation: cache aggressively, back off on 429, and allow manual override/re-match.
- **Misidentified executables during scan** → junk in library. Mitigation: heuristic filtering + mandatory user confirmation (Decision 5).
- **SQLite write contention** between the tracker (ending a session) and UI reads → low risk for single-user desktop; mitigation: WAL mode and short-lived connections/transactions.

## Migration Plan

Greenfield — no data migration. On first run, create `%LOCALAPPDATA%\Mosaic\`, initialize the SQLite schema, and prompt (optionally) for a SteamGridDB API key. Schema changes in future changes use EF Core migrations (or a simple versioned migration step if using `Microsoft.Data.Sqlite` directly).

## Open Questions

- Policy for an unclosed session found at startup: close it at the app's last-known timestamp vs. discard the partial session. **Resolved**: discard the partial session (conservative), implemented in `PlayTracker.ReconcileOpenSessionsAsync`.
- Whether the background "real executable" poller should also detect games launched entirely outside Mosaic (full background watcher) — out of scope for this MVP but the override poller is a step toward it.

## Resolved After MVP

- **Persistence**: EF Core over SQLite (see Decision 2).
- **Job Object launch race** (assign-after-`Process.Start`): deferred from the MVP and now tracked as the follow-up change **`harden-play-tracking-launch`** (start the game suspended, assign to the job, then resume). The per-game "real executable" override mitigates the common cases in the meantime.

## Post-MVP Polish

The MVP was followed by a polish pass (see `tasks.md` §9 and the added requirements in the `artwork`, `game-library`, and `library-ui` specs): a central dark theme and custom `WindowChrome` title bar with the Mosaic logo/icon; library search/sort/context-menu and live running badges; scrollable/resizable dialogs with "Save & Close"; auto-saving scan-folder settings; and substantially smarter artwork resolution and folder scanning (folder-name candidates, diacritic-insensitive similarity, cover-art preference among duplicate entries, CamelCase fallback, title adoption, request throttling, batch "refetch missing", and per-game-folder scan grouping with stronger junk filtering).
