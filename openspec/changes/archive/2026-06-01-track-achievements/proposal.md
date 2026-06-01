## Why

Steam (and every other store) gives players the satisfaction of tracking and unlocking achievements; Mosaic's non-Steam library offers nothing comparable, so the games launched through Mosaic feel "second class" even when they ship a full achievement set. We want Mosaic to do what Steam does — show each game's achievements and detect them unlocking as you play — without the game having to be on Steam.

A feasibility pass established that this is achievable (proven by the Playnite *SuccessStory* plugin and *Achievement Watcher*, which do exactly this for non-Steam libraries) but that there is **no universal API** for "a game's achievements." Achievements are per-platform. The high-leverage, non-Steam-friendly path is: pull the achievement **definitions** from the Steam Web API by appid, and detect **unlocks** by reading the local files that Steam emulators (Goldberg/GSE, SmartSteamEmu, CODEX, CreamAPI, …) write — which is precisely how DRM-free/non-Steam copies of Steamworks games record progress today. A manual mode covers everything else.

## What Changes

- Add a new **achievements** capability: per game, Mosaic can hold a set of achievement *definitions* (name, description, icons, hidden flag) and per-achievement *unlock state* (unlocked + timestamp), persisted locally.
- **Resolve definitions from the Steam Web API.** A game is linked to a Steam appid (auto-matched by name with user confirmation, mirroring the existing artwork-matching flow, with a manual appid override). `GetSchemaForGame` supplies the achievement list and icon URLs, cached locally. Requires a user-supplied Steam Web API key; absent the key, auto-resolution is disabled and manual mode still works (graceful degradation, exactly like the SteamGridDB key today).
- **Detect unlocks from local Steam-emulator files.** Mosaic locates and parses the achievement/stat files written by common Steam emulators (per-game `steam_settings\` and the user-profile save locations such as `%APPDATA%\Goldberg SteamEmu Saves\<appid>\`). Each file yields which achievements are unlocked and when.
- **Live unlock notifications while playing.** When a tracked game starts a play session, Mosaic watches that game's emulator achievement files for changes and raises an in-app "Achievement unlocked" notification in real time, then persists the new unlocks. On session end it does a final reconciling scan.
- **Manual fallback.** For games with no readable achievement source, the user can mark achievements unlocked/locked by hand (and, where no schema exists, define them).
- **Surface progress in the UI.** The library and recently-played grids show an unlocked/total indicator per game; the game detail view shows the full achievement list (unlocked/locked, timestamps, hidden handling) with manual toggles and a refresh action.
- Per-game **opt-out / source override** so a game can disable achievement tracking or force manual mode.

Scope notes (explicit non-goals for this change): no GOG Galaxy, Epic/EOS, or Xbox account integration in v1 (researched and deferred — Epic in particular is not third-party-readable); no runtime DLL injection / process hooking (fragile and anti-cheat-risky — deliberately avoided, as the mature tools do); no global achievement-history/timeline screen.

## Capabilities

### New Capabilities
- `achievements`: linking a game to an achievement schema (Steam appid), resolving achievement definitions (Steam Web API, cached), detecting unlocks from local Steam-emulator files (incl. live watching during a play session), manual unlock marking, per-game source configuration/opt-out, and persistence of definitions + unlock state.

### Modified Capabilities
- `game-library`: editing a game additionally allows configuring its achievement linkage — the Steam appid (or "none"), the achievement source/mode, and an enable/disable toggle — persisted with the game.
- `library-ui`: the library/recently-played grids surface per-game achievement progress (unlocked/total), the game detail view presents the achievement list with manual toggles and a refresh action, and the app shows a live "achievement unlocked" notification during play.

## Impact

- **Models**: new `Achievement` (definition: appid/key, name, description, icon, hidden) and `AchievementUnlock` (game id, achievement key, unlocked-at) — or a combined per-game achievement row; `Game` gains achievement-linkage fields (Steam appid, tracking-enabled/source). EF Core migration required (the app auto-migrates on startup).
- **Services**: new `IAchievementService` / `AchievementService` (schema resolution + caching, emulator-file location/parsing, live watching, manual marking, lifecycle events `AchievementUnlocked` / progress-updated); new `SteamWebApiClient` (schema fetch, throttled, mirroring `SteamGridDbClient`); emulator-file parsers. Hooks into existing `IPlayTracker.SessionStarted/SessionEnded` to start/stop watching — **no change to play-tracking's own behavior**.
- **Settings**: new Steam Web API key setting in `settings.json` / settings UI (alongside the SteamGridDB key); emulator save-path discovery defaults.
- **ViewModels/Views**: `GameTileViewModel` gains achievement-progress; `GameDetailViewModel` gains the achievement list + manual toggles + refresh; a live unlock notification surfaced via `App.RunOnUiAsync` (same background-thread → UI pattern as artwork/session events). Game add/edit dialog gains appid + tracking config.
- **Data dir**: cached achievement icons under `%LOCALAPPDATA%\Mosaic\` (e.g. `achievements\`), consistent with `artwork\`.
- **Tests**: `Mosaic.Tests` — emulator-file parsing (real Goldberg/SSE samples), schema resolution/caching, unlock-diffing + live-watch event raising, manual marking, reconcile-on-session-end, and graceful no-API-key behavior.
- **No external runtime dependency beyond HTTP** (Steam Web API) and local file reads; no injection, no admin rights.
