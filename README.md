# Mosaic

A Windows desktop app for managing and play-tracking your **non-Steam games** — a single library for DRM-free, emulated, itch.io, and launcher games, with accurate automatic play-time tracking and auto-fetched cover art.

Built with **.NET 10** and **WPF**. Windows-only by design (it relies on Windows Job Objects for play tracking and WPF for the UI).

## Features

- **Game library** — add games manually (pick an executable) or by scanning folders; edit and remove entries. Removing a game never touches files on disk.
- **Accurate play-time tracking** — launches a game and tracks the **entire process tree** via a Windows Job Object, so titles that start through a thin launcher (which spawns the real game and exits) are still measured correctly. A per-game *"real executable"* override handles games started through a separate store/launcher process.
- **Recently played & stats** — total play time and last-played are derived from recorded sessions; a recently-played view orders games by last play.
- **Live feedback** — a running badge with a live elapsed timer appears while a game is open.
- **Auto-fetched artwork** — covers, heroes, and logos are fetched from [SteamGridDB](https://www.steamgriddb.com/) and cached locally. Smart matching uses folder-name candidates, diacritic-insensitive scoring, prefers entries that actually have art, and falls back to CamelCase-splitting (`HatinTime` → "A Hat in Time"). You can also set a manual cover, or refetch missing art for the whole library in one click.
- **Smart folder scanning** — groups executables by game folder and presents one clean candidate per game (filtering out crash reporters, redistributables, shader compilers, etc.), gated behind a confirmation step.
- **Polished dark UI** — a consistent dark theme, a custom title bar with the app icon, search/sort, right-click context menus, and resizable, scrollable dialogs.

## Requirements

- Windows 10 (20H1+) or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- *(Optional)* A free [SteamGridDB API key](https://www.steamgriddb.com/profile/preferences/api) for artwork

## Getting started

```powershell
# Build the app and tests
dotnet build Mosaic.sln

# Run the app
dotnet run --project Mosaic.csproj

# Run the test suite
dotnet test
```

On first run, Mosaic creates its data directory and database automatically. To enable artwork, open **Settings**, paste your SteamGridDB API key, and click **Test** then **Save**. Without a key, the app works fully — games just show a placeholder cover.

### Where your data lives

Runtime data is stored under `%LOCALAPPDATA%\Mosaic\`:

| File / folder   | Purpose                                   |
| --------------- | ----------------------------------------- |
| `mosaic.db`     | SQLite database (games, sessions, artwork)|
| `settings.json` | Scan folders and SteamGridDB API key      |
| `artwork\`      | Cached cover/hero/logo images             |

## Architecture

A standard layered MVVM app:

- **Models** (`Models/`) — `Game`, `PlaySession`, `Artwork`.
- **Services** (`Services/`) behind interfaces — `IGameLibrary`, `IPlayTracker` (+ `JobObjectTracker`), `IArtworkService` (+ `SteamGridDbClient`), `ISettingsService`, `IDialogService`. Registered via `Microsoft.Extensions.Hosting` DI in `App.xaml.cs`.
- **ViewModels** (`ViewModels/`) — CommunityToolkit.Mvvm.
- **Views** (`Views/`, `Themes/`, `Theming/`) — XAML, a central dark theme, and a custom `WindowChrome`-based title bar.

**Persistence** uses **EF Core** over SQLite with short-lived `DbContext` instances created per operation from an injected `IDbContextFactory` (no long-lived context), so the background tracker can write sessions concurrently with UI reads. Per-game stats are derived from `PlaySessions`, never stored denormalized. Migrations are applied automatically on startup.

## Project structure

```
Mosaic.csproj            App project (root)
App.xaml(.cs)            Composition root + DI host
Models/  Services/  ViewModels/  Views/
Data/                   EF Core DbContext + migrations
Themes/  Theming/       Dark theme + custom window chrome
Converters/
tools/generate-icon.ps1 Regenerates Mosaic.ico + Mosaic.png
Tests/Mosaic.Tests/     xUnit tests
openspec/               Spec-driven development (specs + change history)
```

## Development

- **Tests:** `dotnet test` (or filter, e.g. `dotnet test --filter "FullyQualifiedName~PlayTrackerTests"`).
- **EF migrations:** `dotnet ef migrations add <Name>` (uses the design-time context factory; the running app migrates on startup).
- **App icon:** edit the palette/layout at the top of `tools/generate-icon.ps1` and re-run it to regenerate `Mosaic.ico` and `Mosaic.png`.
- **Specs & changes:** this project uses **OpenSpec** for spec-driven development. Capability specs live in `openspec/specs/`; proposed work lives under `openspec/changes/`.

## License

Personal project — no license specified.
