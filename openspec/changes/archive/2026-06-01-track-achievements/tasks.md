## 1. Data model & migration

- [x] 1.1 Add `Achievement` model (definition: `GameId`, `ApiName`/key, `DisplayName`, `Description`, `IconUnlockedPath`, `IconLockedPath`, `Hidden`, `Source`, display order) under `Models\`.
- [x] 1.2 Add unlock state — chose unlock fields on `Achievement` (`UnlockedAt`, `IsManualUnlock`) per design Open Question; definition and unlock columns are independent (refresh upserts by `(GameId, ApiName)`, never clears an unlock).
- [x] 1.3 Add `SteamAppId` (nullable int), `AchievementTrackingEnabled` (bool, default true), and `AchievementSource` (enum Auto/Manual/Disabled) to `Game`.
- [x] 1.4 Configure entities in `MosaicDbContext.OnModelCreating`: unique `(GameId, ApiName)` index, required columns, `AchievementTrackingEnabled` default true, and cascade-delete of a game's achievements (mirroring the PlaySession/Artwork cascade).
- [x] 1.5 Generate the EF migration (`20260601103001_AddAchievements`, design-time `MosaicDbContextFactory`); the app applies it on startup via the existing `Database.MigrateAsync()`.

## 2. Settings

- [x] 2.1 Add a Steam Web API key setting to the settings model + `settings.json` (alongside the SteamGridDB key), with empty default.
- [x] 2.2 Surface the key in the settings UI (password box + show-key toggle) and document that an absent key disables auto-resolution (manual mode still works).

## 3. Steam Web API client & schema resolution

- [x] 3.1 Add `SteamWebApiClient` (HTTP, behind a typed `HttpClient`) calling `ISteamUserStats/GetSchemaForGame`; calls serialized by the `AchievementService` `FetchGate` `SemaphoreSlim` (as `ArtworkService` does for SteamGridDB).
- [x] 3.2 Map the schema response to `SteamAchievementDef` (key, name, description, hidden, unlocked/locked icon URLs); `AchievementService` upserts these into `Achievement` rows.
- [x] 3.3 Download + cache achievement icons under `%LOCALAPPDATA%\Mosaic\achievements\` (`AppPaths.AchievementsDirectory`; paths persisted, bytes on disk, cached files reused), reusing the artwork caching approach.
- [x] 3.4 No-key graceful degradation: `IsAutoResolutionAvailable`/`RefreshAsync` no-op without a key; manual mode unaffected.

## 4. Appid linking

- [x] 4.1 `SuggestAppsAsync` proposes appids by name match (reuses `ArtworkService.BuildSearchTerms`/`Similarity` against Steam's `SearchApps`); never links without confirmation (UI confirm in Group 8/9).
- [x] 4.2 `LinkAppIdAsync(gameId, appId)` is the manual-override entry point; `SetUnlinkedAsync` is the explicit "no Steam achievements" state; both persist on the `Game`.

## 5. Emulator-file unlock source

- [x] 5.1 `IAchievementUnlockSource` seam (CanHandle / LocateFiles / ReadUnlocks) so future GOG/Xbox sources are additive.
- [x] 5.2 `SteamEmulatorUnlockSource` location discovery: per-game `<gameDir>\steam_settings\` + `stats\`, plus per-user `Goldberg SteamEmu Saves\<appid>\`, `GSE Saves\<appid>\`, and CODEX/RUNE/SmartSteamEmu analogues.
- [x] 5.3 Goldberg/GSE JSON parser (`EmulatorAchievementParser.ParseGoldbergJson`) → `{apiName, unlockedAt}` unlocked set.
- [x] 5.4 INI parser (`ParseIni`, CODEX/ALI213 per-achievement section + flat `[Achievements]`) and merge across files; unrecognized files ignored.
- [x] 5.5 Timestamp from the file when present (`earned_time`/`UnlockTime`), else detection time (filled in `ScanUnlocksCoreAsync`).

## 6. Achievement service (orchestration, manual, persistence)

- [x] 6.1 Added `IAchievementService` / `AchievementService` singleton + `SteamWebApiClient` HttpClient; registered in `App.ConfigureServices`.
- [x] 6.2 `RefreshCoreAsync` resolves + caches schema on link and on explicit refresh, upserting by `(GameId, ApiName)` and preserving unlock state.
- [x] 6.3 `ScanUnlocksCoreAsync` runs the source, diffs against persisted rows, persists new unlocks (monotonic — absence never re-locks).
- [x] 6.4 Manual marking (`SetUnlockedAsync`, may lock or unlock) and user-defined achievements (`AddManualAchievementAsync`).
- [x] 6.5 Per-game config gating (enabled + Auto/Manual/Disabled) on resolution, scanning and watching.
- [x] 6.6 Derived progress (`GetProgressAsync`) and per-game queries on short-lived `IDbContextFactory` contexts with `AsNoTracking()` reads.
- [x] 6.7 Lifecycle events `AchievementUnlocked` (game + achievement) and `AchievementsChanged` (gameId) raised on background threads.

## 7. Live watching & play-session hooks

- [x] 7.1 `AchievementService` subscribes to `IPlayTracker.SessionStarted`/`SessionEnded` (no change to `PlayTracker`).
- [x] 7.2 On `SessionStarted`: for an enabled, Auto, source-capable game, attaches debounced `FileSystemWatcher`s to the resolved file dirs; on change → re-read + diff → persist → raise `AchievementUnlocked`.
- [x] 7.3 On `SessionEnded`: disposes the watcher and runs a final reconcile `ScanUnlocksAsync` so a missed change is still captured.
- [x] 7.4 No watcher for unlinked / tracking-disabled / manual-only games.

## 8. ViewModels

- [x] 8.1 Added derived achievement progress (`AchievementsUnlocked`/`Total`, `HasAchievements`, `AchievementProgressDisplay`) to `GameTileViewModel`, seeded from `GameListItem` and re-applied via the tile rebuild.
- [x] 8.2 `GameCollectionViewModel` subscribes to `AchievementsChanged` via `App.RunOnUiAsync` and updates the matching tile's progress by game id (`GetProgressAsync`).
- [x] 8.3 `GameDetailViewModel` exposes the achievement list (`AchievementItemViewModel` with hidden masking, icons, timestamps), manual-toggle, scan, and refresh commands.
- [x] 8.4 Live unlocks surface as a toast in `MainViewModel` (single instance, so the two collections don't double-notify) via `App.RunOnUiAsync` on `AchievementUnlocked`, auto-hidden by a `DispatcherTimer`.
- [x] 8.5 Appid / source / tracking-enabled in `GameDetailViewModel` (edit) applied immediately via the service; optional appid in `AddGameViewModel` (add) flows through `AddGameRequest` and triggers resolution.

## 9. Views (XAML)

- [x] 9.1 Achievement progress indicator added to both tile templates (`LibraryView.xaml` bottom-right pill clear of running/fetch badges; `RecentlyPlayedView.xaml` text line); hidden when the game has no achievements.
- [x] 9.2 Achievement list UI added to `GameDetailWindow.xaml` (icon, masked name/description, unlock time, per-row toggle, refresh + scan buttons, add-manual row).
- [x] 9.3 Live "achievement unlocked" toast overlay added to `MainWindow.xaml` (icon + title + game, drop shadow, click-to-dismiss).
- [x] 9.4 Appid + tracking-enabled/source controls in `GameDetailWindow` (with the "Find on Steam" user-confirm step) and an optional Steam App ID field in `AddGameWindow`.

## 10. Tests

- [x] 10.1 `EmulatorAchievementParserTests`: Goldberg JSON (earned-only + unix timestamp; schema-array/garbage ignored) and INI (per-achievement section + flat `[Achievements]`; meta keys/garbage ignored).
- [x] 10.2 `Refresh_ResolvesSchema_CachesIcons_AndPreservesUnlocksAcrossRefresh`: definitions mapped, icons cached on disk, second refresh updates a definition and adds a new one while preserving the unlock.
- [x] 10.3 `Scan_DetectsNewUnlock_RaisesEvent_AndIsMonotonic`: new unlock detected/persisted/event-raised; a later scan with the file gone does not re-lock or re-raise. (+`Scan_UsesDetectionTime_WhenFileHasNoTimestamp`.)
- [x] 10.4 `SessionEnded_ReconcileScan_CapturesAnUnlockMissedWhileWatching` covers the session-end reconcile; detection+`AchievementUnlocked` covered by the scan test. (The live `FileSystemWatcher` firing is timing-dependent and left to manual verification — it simply calls the same `ScanUnlocksAsync`.)
- [x] 10.5 `ManualMode_AddAndToggle_WorksWithoutSchema_AndSurvivesRefresh`: toggle updates progress, manual unlock + manual definition survive a refresh, manual re-lock works.
- [x] 10.6 `NoApiKey_DisablesResolution_ButManualStillWorks`: no key → no HTTP/no resolution; manual add + unlock still work.
- [x] 10.7 `RemovingGame_CascadesAchievements_AndDeletesCachedIcons`: rows cascade-deleted and the cached icon file removed.

## 11. Verification

- [x] 11.1 `dotnet build Mosaic.sln` and `dotnet test` green — build 0 errors/0 warnings; **46/46** tests pass (incl. the 13 new achievement/UI tests).
- [x] 11.2 Ran the built app and confirmed by observation (captured the window): startup applied the EF migration to the real DB without error; the Library renders with the new tile template (achievement pill correctly hidden when a game has no achievements); the game-detail window renders the full new **Achievements** section — summary, Find on Steam / Refresh / Scan unlocks, Steam App ID + Link/Unlink, "Track achievements" checkbox (default checked) + Mode combo (Auto), and the add-manual row — with all bindings resolving and no XAML/resource load errors. (Fixed a header layout nit found during observation so all three action buttons show.) The deeper interactive flows — live Steam resolution against the user's own API key, real emulator-file unlocks, and the live toast — are left to the user to exercise on their real library/key; that logic is covered by the 12 new unit tests (parsing, schema resolve+cache, monotonic scan, session-end reconcile, manual marking, no-key degradation, removal cleanup).

## 12. Prevent overlapping achievement actions (follow-up, user-requested)

- [x] 12.1 Add a shared `IsAchievementBusy` flag on `GameDetailViewModel`, set at the start of each achievement-mutating command and reset in a `finally`. Each such command's `CanExecute` returns `!IsAchievementBusy` (`CanRunAchievementAction`), with `[NotifyCanExecuteChangedFor]` re-evaluating them when the flag flips — so while any one runs (Find on Steam / Refresh / Scan unlocks / Link / Unlink / Add manual / per-row toggle) the others' bound buttons disable and no two achievement updates can overlap. (The "Track achievements" checkbox and Mode dropdown are not gated — they only flip flags, not a fetch/scan.)
- [x] 12.2 Regression test `GameDetailBusyTests.RunningOneAchievementAction_DisablesAllAchievementCommands_UntilItCompletes` (gated fake `IAchievementService`): every achievement command reports `CanExecute == false` while a scan is in flight, then `true` again once it completes. Build clean; **46/46** tests pass.
- [x] 12.3 Moved the detail window's **Remove** / **Save & Close** actions out of the scrolling content into a pinned footer (`GameDetailWindow.xaml`): the editor + achievement list now scroll in a `ScrollViewer` (root grid row 0) while the action bar stays fixed at the bottom (row 1), so the long achievement list no longer pushes Save/Remove off-screen. Verified by observation against the running app.
