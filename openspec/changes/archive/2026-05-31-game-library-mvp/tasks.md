## 1. Project Foundation

- [x] 1.1 Add NuGet packages: `CommunityToolkit.Mvvm`, `Microsoft.Extensions.Hosting` (DI), and `Microsoft.EntityFrameworkCore.Sqlite` (+ `Microsoft.EntityFrameworkCore.Design` for migrations)
- [x] 1.2 Set up the generic host / DI container in `App.xaml.cs` and register service interfaces (`IGameLibrary`, `IPlayTracker`, `IArtworkService`, settings)
- [x] 1.3 Create the app data directory under `%LOCALAPPDATA%\Mosaic\` (database + `artwork\` cache) on startup
- [x] 1.4 Define core domain models: `Game`, `PlaySession`, `Artwork`

## 2. Persistence

- [x] 2.1 Define the EF Core `DbContext`, entity configurations (cascade delete game → sessions/artwork), and the initial migration for `Games`, `PlaySessions`, `Artwork`; enable WAL mode
- [x] 2.2 Implement the games persistence (short-lived DbContext per operation, lazy-init after window shows) with create/read/update/delete
- [x] 2.3 Implement persistence for play sessions (insert open session, update on close, query by game)
- [x] 2.4 Implement aggregate queries: total play time per game (SUM) and last-played (MAX)

## 3. Play Tracking (core — build and validate first)

- [x] 3.1 Implement `JobObjectTracker`: P/Invoke for `CreateJobObject`, `AssignProcessToJobObject`, completion-port association, using `SafeHandle`s; raise an event when the last process in the job exits
- [x] 3.2 Implement `IPlayTracker.Launch(game)`: start the executable (args/working dir), assign to a job object, and record session `StartedAt` immediately (per play-tracking spec)
- [x] 3.3 End the session and persist `EndedAt`/duration when the whole process tree exits
- [x] 3.4 Implement the real-executable override: poll for the configured executable name to determine the active session period when it differs from the launched process
- [x] 3.5 Implement startup reconciliation of any unfinished (open) session per the design's policy
- [x] 3.6 Add an integration test using a stub launcher (parent exits early, child runs) verifying the session spans the child's lifetime; cover the missing-executable error path

## 4. Game Library Service

- [x] 4.1 Implement add-game-manually (pick `.exe`, default name, optional args/working dir/real-exe) with duplicate-path rejection
- [x] 4.2 Implement edit-game and remove-game (cascade-delete sessions/artwork; never touch files outside Mosaic's data dir)
- [x] 4.3 Implement folder scanning: enumerate executables, filter known non-game executables (uninstallers, crash handlers, redistributables), exclude already-known paths
- [x] 4.4 Return scan candidates for confirmation and add only user-confirmed candidates

## 5. Artwork

- [x] 5.1 Implement `IArtworkService` SteamGridDB client (`HttpClient`): search by name, fetch grid/hero/logo, with 429 back-off
- [x] 5.2 Download artwork to the local cache and store local paths in the DB; reuse cached art instead of re-downloading
- [x] 5.3 Trigger async best-effort fetch on game add; show placeholder on failure/no-match and when no API key is configured
- [x] 5.4 Implement manual artwork override and ensure auto-fetch never replaces an override

## 6. Settings

- [x] 6.1 Implement persisted user settings: scan folders list and SteamGridDB API key
- [x] 6.2 Build a settings view to edit scan folders and the API key

## 7. UI (MVVM)

- [x] 7.1 Build the app shell in `MainWindow.xaml` (navigation between Library / Recently Played / Settings)
- [x] 7.2 Build the library view: grid of games showing artwork (or placeholder) and name; empty-state guidance to add/scan
- [x] 7.3 Build the add-game and folder-scan flows (file/folder pickers + candidate confirmation list)
- [x] 7.4 Build the game detail view: name, artwork, total play time, last played, and a Launch button wired to `IPlayTracker`
- [x] 7.5 Build the recently-played view ordered by last-played, updating after a session completes

## 8. Integration & Verification

- [x] 8.1 Wire viewmodels to services via DI; verify add → artwork appears → launch → play time and last-played update across the UI
- [x] 8.2 Verify library and play history persist across an app restart
- [x] 8.3 Run `openspec validate game-library-mvp` and confirm all spec scenarios are covered by the implementation

## 9. Polish & Refinements (post-MVP iteration)

- [x] 9.1 Central dark theme (`Themes/DarkTheme.xaml`): themed Button/TextBox/PasswordBox/ComboBox/CheckBox/ListBox/ScrollBar/ContextMenu/ToolTip with hover/press/focus states
- [x] 9.2 Custom title bar via `WindowChrome` + `MosaicWindow` base (logo, themed min/max/close, maximize-clip fix); dark chrome on all windows
- [x] 9.3 App icon: generated multi-resolution `Mosaic.ico` + high-res `Mosaic.png` (tools/generate-icon.ps1); used for window/taskbar and the in-app sidebar logo
- [x] 9.4 Library UX: search box, sort (name / most played / recently played / recently added), right-click context menu (Play / Details / Remove)
- [x] 9.5 Play feedback: `SessionStarted` event, running badge with live per-second timer, Play disabled while running
- [x] 9.6 Forms & dialogs: masked API key with show toggle + Test key button, inline Add-Game validation, real-executable helper text; scrollable + resizable Add-Game and Game-Detail windows; placeholder text baked into the themed TextBox (pixel-aligned)
- [x] 9.7 Settings: scan folders auto-save on add/remove (API key stays on Save)
- [x] 9.8 Game detail: "Save & Close", "Set cover", "Refetch art"
- [x] 9.9 Smarter artwork search: folder-name candidate terms, diacritic-insensitive token-coverage similarity, prefer a match that has cover art (handles duplicate SteamGridDB entries), CamelCase last-resort fallback
- [x] 9.10 Adopt the matched SteamGridDB title as the game name for path-derived names; keep user-typed names
- [x] 9.11 Throttle SteamGridDB access (batch adds no longer rate-limited); "Refetch missing artwork" toolbar action for the whole library
- [x] 9.12 Smarter folder scan: group executables by game folder, stronger junk filtering, pick the best executable per folder, skip folders already in the library
- [x] 9.13 Tests for search/scan/scoring (24 passing); behaviour verified against the live app and the real SteamGridDB API
