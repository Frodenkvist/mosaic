## Why

PC gamers accumulate many games outside of Steam — DRM-free titles, emulated games, itch.io downloads, pirated/portable builds, and games from launchers they'd rather not keep open. These have no single home: there's no unified library, no cover art, no "recently played", and no play-time history the way Steam provides. Mosaic gives non-Steam games a first-class library with automatic, accurate play-time tracking.

## What Changes

- Introduce a persistent **game library**: add games manually (pick an executable) or by scanning folders, store metadata locally, and remove/edit entries.
- **Launch games from Mosaic** and **track play time accurately** using Windows Job Objects to follow the whole process tree (so launcher-spawns-game cases are measured correctly), with a per-game "real executable" override for stubborn cases.
- Record **play sessions** (start/end/duration) and derive **stats**: total play time per game, last-played timestamp, and a **recently played** view.
- **Auto-fetch artwork** (covers, grids, logos) from SteamGridDB so the library looks complete without manual work, with manual override.
- Provide the **library UI**: a grid/list of games with cover art, a detail view (play time, last played, launch button), and a recently-played section — a usable MVP in one change.

## Capabilities

### New Capabilities
- `game-library`: Managing the set of games — adding (manual + folder scan), editing, removing, and persisting game entries and their metadata.
- `play-tracking`: Launching games and measuring play time via process-tree monitoring, recording play sessions, and deriving per-game totals and last-played.
- `artwork`: Fetching and caching cover/grid/logo art from SteamGridDB, with manual artwork override.
- `library-ui`: The desktop views — library grid/list, game detail, and recently-played — that surface the data above.

### Modified Capabilities
<!-- None — this is a greenfield app with no existing specs. -->

## Impact

- **Codebase**: Greenfield WPF app (Mosaic, .NET 10, `UseWPF`). Adds data models, persistence, a process-monitoring service, an HTTP client for SteamGridDB, and XAML views/viewmodels. `MainWindow.xaml` (currently empty) becomes the app shell.
- **Dependencies (new)**: local persistence (SQLite or JSON file store), an MVVM helper (e.g. CommunityToolkit.Mvvm), `System.Net.Http` for SteamGridDB, and Win32 Job Object interop (P/Invoke, no package).
- **External services**: SteamGridDB API (requires a free API key; library degrades gracefully without one).
- **Platform**: Windows-only (Job Objects + WPF). Requires local storage for the database and an artwork cache directory.
- **Configuration**: User settings for scan folders and SteamGridDB API key.
