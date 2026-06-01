## Context

Mosaic launches and play-tracks **non-Steam** games. There is no universal way to enumerate an arbitrary executable's achievements or detect unlocks — achievements are implemented per-platform (Steamworks, GOG Galaxy, Epic/EOS, Xbox, or bespoke save files). A feasibility pass confirmed the goal is achievable and is solved prior art:

- **Playnite + SuccessStory** — a non-Steam library/launcher (Mosaic's closest analog) with a mature achievements plugin spanning Steam, GOG, Epic, Xbox, RetroAchievements, *Steam emulators*, and manual.
- **Achievement Watcher** — real-time unlock toasts by parsing the local files written by Steam emulators (CODEX, Goldberg/GSE, SmartSteamEmu, CreamAPI…), using the Steam Web API for the achievement names/icons.

The decisive constraints for Mosaic's non-Steam premise:
- **Definitions** (the list: name, description, icon, hidden flag) are reliably obtainable from the **Steam Web API** `ISteamUserStats/GetSchemaForGame` given a Steam **appid** and a user-supplied Steam Web API key.
- **Unlock state** for a non-Steam copy is *not* on any Steam account; it lives in the **local files** the bundled Steam emulator writes (per-game `steam_settings\` and user-profile saves such as `%APPDATA%\Goldberg SteamEmu Saves\<appid>\` / `…\GSE Saves\<appid>\`). Reading/watching those files is the only viable automatic unlock source.
- Everything else (no emulator, no schema, store-locked data) falls back to **manual**.

The existing codebase gives us the patterns to reuse wholesale: the SteamGridDB integration (an HTTP client behind an interface, a `SemaphoreSlim` gate, a user-supplied key that gracefully disables auto-fetch when absent, name-similarity matching, cached files under the data dir); the artwork lifecycle-event model (`ArtworkUpdated`/`ArtworkFetchStarted/Failed` raised on background threads, marshalled to the UI via `App.RunOnUiAsync`); and the play-tracker's `SessionStarted`/`SessionEnded` events plus the short-lived `IDbContextFactory<MosaicDbContext>` persistence pattern.

## Goals / Non-Goals

**Goals:**
- Per game, hold achievement **definitions** + **unlock state**, persisted locally and survivable across restarts and crashes.
- Resolve definitions from the Steam Web API by appid, with appid auto-matched by name (user-confirmed, like artwork) and a manual override; cache the schema + icons locally.
- Detect unlocks automatically by locating and parsing common Steam-emulator achievement files; diff against known state to find newly-unlocked achievements with their timestamps.
- Show **live** "achievement unlocked" notifications during a play session (file-watching), plus a reconciling scan when the session ends.
- Manual unlock/lock marking, and per-game enable/disable + source override.
- Graceful degradation with no Steam Web API key (manual mode intact), mirroring the SteamGridDB-key behavior.
- Reuse existing threading (`App.RunOnUiAsync`), persistence (short-lived contexts), and HTTP-client/gate conventions; **no change to play-tracking's own behavior** — the achievement watcher only *subscribes* to its session events.

**Non-Goals:**
- GOG Galaxy DB, Epic/EOS, Xbox/MS Store, EA, or RetroAchievements sources (researched; deferred — Epic/EOS is not third-party-readable at all, the others need account auth). The source abstraction is designed to admit them later, but none ship in v1.
- Runtime DLL injection / hooking `steam_api.dll` exports. Deliberately excluded: fragile per-version, trips anti-cheat, and the mature tools all avoid it.
- A global achievement timeline/history screen, achievement comparison, or social features.
- Editing achievement *definitions* for Steam-sourced games (definitions are read-only from the schema; only manual-mode games allow user-defined achievements).
- Re-deriving or back-filling unlock timestamps for sessions before this feature existed.

## Decisions

### 1. `IAchievementService` orchestrates; sources are pluggable; play-tracking is untouched
A new singleton `IAchievementService` / `AchievementService` owns: schema resolution + caching, choosing & running an unlock **source** for a game, live watching, manual marking, and raising lifecycle events. Unlock sources implement a small internal interface (e.g. `IAchievementUnlockSource` with "can handle this game / locate files / read current unlock set / produce a watcher"). v1 ships exactly one automatic source — **Steam-emulator files** — plus the manual path.
- The service **subscribes** to the existing `IPlayTracker.SessionStarted`/`SessionEnded`; it does not modify the tracker. On `SessionStarted(gameId)` it starts watching that game's files; on `SessionEnded(gameId)` it stops the watcher and runs a final reconcile scan.
- Rationale: keeps play-tracking's hard-won Job-Object logic and its spec unchanged; isolates all achievement concerns; the source interface is the seam for future GOG/Xbox sources without reopening this design.
- Alternative considered: fold watching into `PlayTracker`. Rejected — couples unrelated concerns and would mutate the play-tracking spec/behavior.

### 2. Data model: read-only definitions + per-game unlock rows, both persisted
Add EF entities:
- `Achievement` — a **definition** scoped to a game: `GameId`, `ApiName` (the stable Steam achievement key), `DisplayName`, `Description`, `IconUnlockedPath`, `IconLockedPath`, `Hidden`, `Source` (Steam/Manual), display order. Icons cached as files under `%LOCALAPPDATA%\Mosaic\achievements\` (paths stored, bytes on disk — same as artwork).
- `AchievementUnlock` — state: `AchievementId` (FK), `UnlockedAt` (nullable; null = locked), `Source`/`Manual` flag. (Implementation may merge unlock state onto the `Achievement` row; kept conceptually separate here because Steam definitions are read-only/refreshable while unlock state is user/emulator-owned and must survive a schema re-fetch.)
- `Game` gains `SteamAppId` (nullable int), `AchievementTrackingEnabled` (bool, default true), and `AchievementSource` (enum: Auto/Manual/Disabled).
- Per-game progress (unlocked/total) is **derived** from these rows, never denormalized — consistent with how play stats are derived from `PlaySessions`. Removing a `Game` cascade-deletes its achievements + unlocks + cached icons (extend `OnModelCreating` + the existing data-dir cleanup).
- One EF migration via `MosaicDbContextFactory`; the running app auto-applies it on startup (`Database.MigrateAsync()`).
- Rationale: separating definition from unlock state lets a schema refresh replace definitions without touching the user's unlock history, and lets a manual unlock coexist with a Steam-sourced definition. Deriving progress avoids reconciliation bugs.

### 3. Schema from Steam Web API; appid linked by name-match (user-confirmed) + manual override
A new `SteamWebApiClient` calls `GetSchemaForGame(appid, key)` and returns definitions + icon URLs, behind a `SemaphoreSlim` gate (serialize calls so batch operations aren't rate-limited) — structurally a sibling of `SteamGridDbClient`.
- **Appid resolution**: reuse the artwork-style approach — match the game's name against Steam's app list to propose an appid, **user-confirms** (no silent linking), with a manual appid field in the edit dialog as the authoritative override. A game may also be explicitly "no Steam achievements" (→ manual/disabled).
- **Key handling**: the Steam Web API key is a new `settings.json` setting beside the SteamGridDB key. Absent key → schema auto-resolution disabled, a clear status surfaced, manual mode fully functional. Mirrors the documented SteamGridDB graceful-degradation behavior exactly.
- Definitions + icons are **cached**; refresh is explicit (a per-game "refresh achievements" action) or on first link. We do not hammer the API on every launch.
- Rationale: the Steam Web API is the only broadly-usable definitions source and the key/gate/caching/matching machinery already exists for SteamGridDB — we clone a proven pattern rather than invent one.
- Alternative considered: scrape SteamDB for names. Rejected — fragile, unofficial, and the Web API is the supported route (the public cache server Achievement Watcher once used was shut down, which is *why* it now requires a user key).

### 4. Unlock detection: locate & parse emulator files, diff to find new unlocks
The Steam-emulator source, given a game's appid + install dir, searches the known locations:
- per-game: `<gameDir>\steam_settings\achievements.json` and emulator save subfolders next to `steam_api(64).dll`;
- per-user: `%APPDATA%\Goldberg SteamEmu Saves\<appid>\`, `%APPDATA%\GSE Saves\<appid>\`, and the analogous CODEX/SmartSteamEmu/CreamAPI locations.
It parses the file(s) into a set of `{apiName, unlockedAt}` and **diffs** against the persisted unlock rows; new entries → unlocks (with timestamp when the file provides one, else "now"). Parsers are per-format (Goldberg/GSE JSON; INI-style for several others) behind the source, each unit-tested against real sample files.
- Rationale: file diffing is robust, requires no injection/admin, and is exactly how the reference tools achieve it. Multiple candidate locations are merged (a game may write more than one).
- Alternative considered: parse only one canonical location. Rejected — emulators and repacks disagree on where they write; a location list maximizes coverage.

### 5. Live watching scoped to the play session, with debounce + final reconcile
On `SessionStarted(gameId)`: if the game has an active emulator source, attach a `FileSystemWatcher` to the resolved file(s)/folder(s). On change (debounced ~250–500ms to coalesce rapid writes), re-read + diff; for each genuinely new unlock, persist it and raise `AchievementUnlocked(gameId, achievement)`. On `SessionEnded(gameId)`: dispose the watcher and run one final full scan (a write can land in the gap, and watchers are not 100% reliable). Watcher work runs off the UI thread; the event is marshalled via `App.RunOnUiAsync` to drive the in-app toast — the identical background→UI pattern used for `SessionStarted`/`ArtworkUpdated`.
- Rationale: scoping the watcher to the session bounds resource use and matches when unlocks can actually happen; the final reconcile makes correctness not depend on the watcher firing.
- Alternative considered: poll the files on a timer during play. Rejected as a primary mechanism (laggy/inefficient) but the final-scan-on-end is effectively a one-shot reconcile; a slow-poll fallback can be added if a watcher proves unreliable for a given location.

### 6. Manual marking coexists with auto, and is never silently overwritten
Manual unlock/lock toggles set the unlock row with `Source = Manual`. A schema refresh replaces *definitions* but preserves unlock state. For an Auto-source game, an emulator-detected unlock and a manual unlock are both "unlocked"; we do not let a re-scan *re-lock* something (absence in the file does not clear an existing unlock) — unlocks are monotonic, matching player expectation and Steam's own behavior.
- Rationale: mirrors the artwork rule that manual overrides are never replaced by auto-fetch; avoids the nasty "my achievement disappeared" failure mode.

### 7. UI surfacing reuses existing tile/detail/notification plumbing
- `GameTileViewModel` gains derived achievement progress (unlocked/total) shown as a subtle indicator on library + recently-played tiles, placed to not collide with the running/fetch badges.
- `GameDetailViewModel` gains the achievement list (unlocked/locked, icons, timestamps, hidden-until-unlocked handling), manual toggles, and a "refresh achievements" command.
- Live unlocks surface as an in-app notification via `App.RunOnUiAsync`; exact visual (toast/overlay) settled during implementation + visual verification (per the project's verify-by-observation preference).
- Game add/edit dialog gains the appid + tracking-enabled/source fields (the `game-library` delta).

## Risks / Trade-offs

- **Emulator file formats drift / new emulators appear** → per-format parsers behind the source interface, each covered by sample-file unit tests; an unrecognized file is ignored (game still works, just shows no auto unlocks) and a new parser is an additive change.
- **Wrong appid → wrong/empty achievement list** → appid linking is user-confirmed (never silent) and manually overridable; a clearly-wrong match is correctable in the edit dialog, and "no achievements" is a valid explicit state.
- **`FileSystemWatcher` misses events or fires storms** → debounce coalesces storms; the **final reconcile scan on session end** guarantees eventual correctness even if no event fired; unlocks are monotonic so a missed-then-recovered read can't lose state.
- **No Steam Web API key** → auto-resolution disabled with a clear status; manual mode fully works; identical to the documented SteamGridDB no-key path. Trade-off: out-of-the-box value needs the user to obtain a free key.
- **Anti-cheat / bans** → we only read files and call a public HTTP API; **no injection, no process tampering, no admin** — nothing anti-cheat reacts to. This is the explicit reason hooking was rejected.
- **Background events racing a disposed/rebuilt view model** → no new exposure; reuse the existing `App.RunOnUiAsync` marshalling + lookup-by-game-id guarding already used for session/artwork events.
- **Unlock timestamps unreliable from some emulators** → store the file's timestamp when present, else the detection time; never block an unlock on having a precise timestamp.
- **Steam Web API rate limits during batch refresh** → the `SemaphoreSlim` gate serializes calls (same mitigation SteamGridDB uses) and schema is cached, so launches don't re-fetch.
- **Hidden/spoiler achievements** → respect the schema's `hidden` flag (mask name/description until unlocked) so the detail view doesn't spoil.

## Migration Plan

One additive EF migration (new `Achievement`/`AchievementUnlock` tables + new nullable `Game` columns) generated via `MosaicDbContextFactory` and auto-applied on startup; no backfill of historical data. New `settings.json` key (Steam Web API key) defaults to empty → feature degrades to manual, so existing installs are unaffected until a key is added and games are linked. Rollback is a revert plus dropping the new tables/columns; cached achievement icons live only under `%LOCALAPPDATA%\Mosaic\achievements\` and are safe to delete (only Mosaic-owned files, per the existing data-dir rule).

## Open Questions

- **Achievement entity shape**: separate `AchievementUnlock` table vs unlock fields on the `Achievement` row — decide at implementation; the spec only requires definition and unlock state to be independently persisted (Decision 2).
- **Initial emulator coverage**: which parsers ship in v1 (Goldberg/GSE is mandatory; CODEX/SmartSteamEmu/CreamAPI are high-value next) — finalize from sample availability; the interface makes the set extensible.
- **Live-notification visual** (toast vs in-window overlay, sound, stacking for rapid multi-unlocks) — finalize during implementation and confirm by running the app.
- **Watcher reliability per location** — whether any save location needs a slow-poll fallback in addition to the watcher + end-of-session reconcile; decide during verification.
