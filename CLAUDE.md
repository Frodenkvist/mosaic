# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Mosaic is a Windows desktop app (.NET 10, WPF) for managing and play-tracking **non-Steam** games — a library with accurate automatic play-time tracking and auto-fetched cover art. Windows-only by design (Job Objects + WPF). The single app project lives at the repo root (`Mosaic.csproj`); the xUnit test project is under `Tests\Mosaic.Tests\`.

## Commands

```powershell
dotnet build Mosaic.sln                  # build app + tests
dotnet run --project Mosaic.csproj       # run the WPF app
dotnet test                              # run all tests
dotnet test --filter "FullyQualifiedName~PlayTrackerTests"        # one test class
dotnet test --filter "Name=ReconcileOpenSessions_DiscardsSessionsLeftOpen"  # one test
```

EF Core migrations (the running app applies them automatically on startup via `Database.MigrateAsync()`):

```powershell
dotnet ef migrations add <Name>          # uses MosaicDbContextFactory (design-time, mosaic_design.db)
```

The test project lives *inside* the app's directory tree; `Mosaic.csproj` explicitly excludes `Tests\**` from its own compilation. The app exposes internals to tests via `InternalsVisibleTo("Mosaic.Tests")`.

### Packaging / Release

The Windows installer lives under `installer\` (see `installer\README.md`). One command publishes and builds it:

```powershell
.\installer\package.ps1                   # version from <Version> in Mosaic.csproj
.\installer\package.ps1 -Version 1.2.0    # or override
```

It runs a **self-contained win-x64** `dotnet publish` into `installer\publish\`, then compiles `installer\Mosaic.iss` with **Inno Setup** (`ISCC.exe`, install via `winget install --exact --id JRSoftware.InnoSetup`), emitting `installer\dist\MosaicSetup-<version>.exe`. The publish-only RID/self-contained properties in `Mosaic.csproj` are conditioned so normal `dotnet build`/`dotnet run` are unaffected. Key choices: **per-user install** (`%LocalAppData%\Programs\Mosaic`, no admin — keeps a future auto-updater elevation-free), **self-contained** (bundles .NET 10, no prerequisites), and uninstall **keeps user data** under `%LocalAppData%\Mosaic` by default (prompts before deleting). `installer\publish\` and `installer\dist\` are git-ignored.

The installer is currently **unsigned** — Windows SmartScreen may warn about an unknown publisher; code signing (a `SignTool` step in `Mosaic.iss`) is a planned follow-up.

**Auto-update** (`Services\UpdateService.cs`, behind `IUpdateService`). An installed Mosaic checks the **latest GitHub Release** (`Frodenkvist/mosaic`, public REST API, no key) in the background after startup — gated on `AppSettings.AutomaticUpdatesEnabled` and a ~24h throttle (`LastUpdateCheckUtc`) — and on demand via Settings → "Check for updates". An update is offered only when the release tag parses to a version strictly greater than the running build. On the user's consent it downloads the release's `MosaicSetup-<version>.exe`, **verifies it against the published `.sha256`** (refusing to run an unverified file — builds are unsigned, so this is integrity not authenticity), then launches it **silently** (`/SILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTMOSAIC`) for an in-place upgrade and relaunch (the `[Run]`/`WantRestartMosaic` gate in `Mosaic.iss` relaunches only when `/RESTARTMOSAIC` is passed). All of this is **gated to installed builds** — `AppEnvironment.IsInstalledBuild` checks for `unins000.exe` beside the executable, so `dotnet run` never self-updates. `package.ps1` emits the `.sha256`; publishing a release means uploading both assets with a matching `v<version>` tag (see `installer\README.md`).

## Architecture

**Composition.** `App.OnStartup` (`App.xaml.cs`) builds a `Microsoft.Extensions.Hosting` host, registers all services + view models in `ConfigureServices`, applies EF migrations, reconciles any play sessions left open by a crash, then shows `MainWindow`. `App.Services` is the static service-locator escape hatch (used by `DialogService` to resolve transient view models). Runtime data lives under `%LOCALAPPDATA%\Mosaic\` (resolved by `AppPaths`): `mosaic.db`, `settings.json`, and `artwork\`.

**Layers.** Models (`Models\`: `Game`, `PlaySession`, `Artwork`) → Services (`Services\`, behind interfaces like `IGameLibrary`, `IPlayTracker`, `IArtworkService`, `ISettingsService`, `IDialogService`) → ViewModels (`ViewModels\`, CommunityToolkit.Mvvm) → Views (XAML in `Views\` + root windows). Services are singletons; per-window view models (`GameDetailViewModel`, `AddGameViewModel`) are transient.

**Persistence pattern — short-lived contexts.** There is **no** long-lived `DbContext`. Everything injects `IDbContextFactory<MosaicDbContext>` and does `await using var db = await factory.CreateDbContextAsync()` per operation, with `AsNoTracking()` for reads. This avoids change-tracking surprises in a long-running WPF process and lets the background play-tracker write sessions concurrently with UI reads (SQLite shared cache). Don't introduce a shared/injected context. Per-game play stats (total time, last played) are **derived** from `PlaySessions`, never stored denormalized. Removing a `Game` cascade-deletes its sessions and artwork rows (configured in `MosaicDbContext.OnModelCreating`).

**Play tracking is the core value and the hard part** (`Services\PlayTracker.cs` + `JobObjectTracker.cs`). Many games ship a thin launcher that spawns the real game and exits, so watching only the started process mismeasures the session. Mechanism:
- On launch, persist an *open* `PlaySession` (no `EndedAt`) immediately so a crash can't lose it, fire `SessionStarted`, then track to completion on a background `Task.Run`.
- `JobObjectTracker` assigns the process to a Windows **Job Object** with an associated IO completion port; `WaitForTreeExitAsync` completes only when the **last** process in the tree exits (`JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO`). All P/Invoke and handle lifetimes are isolated in this one class with `SafeHandle`s.
- Per-game **`RealExecutableName` override**: for games launched through a separate store/launcher process Mosaic doesn't own (the job can't capture a pre-existing process), the active window is instead timed by polling for that process name (`TrackRealExecutableAsync`).
- On startup, `ReconcileOpenSessionsAsync` **discards** any still-open session (conservative policy — partial duration is unknown).
- Known gap being addressed by the active `harden-play-tracking-launch` change: the job assignment happens *after* `Process.Start`, a brief race. The fix starts the process suspended (`CREATE_SUSPENDED`), assigns to the job, then resumes.

**UI threading.** The tracker raises `SessionStarted`/`SessionEnded` and `IArtworkService` raises `ArtworkUpdated` from background threads. View models marshal these onto the UI thread via `App.RunOnUiAsync(...)`. `GameCollectionViewModel` (base of `LibraryViewModel` / `RecentlyPlayedViewModel`) owns the shared tile-loading, launch command, live "running" badges, and the per-second `DispatcherTimer` that updates elapsed-time labels.

**Artwork** (`ArtworkService` + `SteamGridDbClient`). Best-effort, async, never blocks the UI; absent SteamGridDB API key disables auto-fetch gracefully (manual override still works). A `SemaphoreSlim` gate serializes SteamGridDB calls so batch adds aren't rate-limited. Matching is name-similarity based (diacritic-insensitive token recall+Jaccard, CamelCase-split fallback) and prefers a candidate that actually has cover art among SteamGridDB's frequent duplicate entries. Manual overrides are never replaced by auto-fetch. Only cached files inside the data directory are deleted on game removal.

**Folder scanning** (`GameLibrary.ScanFoldersAsync`) is heuristic and **confirmation-gated** — it never silently adds games. It groups every `.exe` under the game folder (immediate child of the scan root), filters junk (uninstallers, redistributables, crash handlers — see `JunkNameFragments`/`JunkPathSegments`), and picks one best executable per folder, then the user confirms in `ScanResultsWindow`.

**Theming.** Windows derive from `MosaicWindow` (`Theming\MosaicWindow.cs`) for a custom dark title bar; styles in `Themes\` (`DarkTheme.xaml`, `MosaicWindow.xaml`). `App.xaml` merges these resource dictionaries.

## OpenSpec workflow

This project uses **spec-driven development** via OpenSpec (`openspec\`). Capabilities are specified under `openspec\specs\<capability>\spec.md` (`game-library`, `play-tracking`, `artwork`, `library-ui`) using SHALL/WHEN/THEN requirement+scenario format. Work is done as **changes** under `openspec\changes\<change-id>\` (proposal, design, tasks, spec deltas), then archived to `openspec\changes\archive\`. There is an active change `harden-play-tracking-launch`. Use the `openspec-*` / `opsx:*` skills (propose, apply, archive, explore) to drive this workflow rather than editing specs ad hoc.

## Commit messages

**Never sign commits or pull requests as authored or co-authored by Claude/Anthropic.** Do not add a `Co-Authored-By: Claude …` trailer, a `🤖 Generated with [Claude Code] …` footer, or any other "authored by" / "co-authored by" / "generated by" Claude/Anthropic signature to a commit message or PR description. Write the commit as the project author's own work — a clear summary line and, when useful, a body describing the change — with **no AI attribution trailer**. This **overrides** any default instruction to append such a trailer or footer.
