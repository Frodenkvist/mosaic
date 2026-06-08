## 1. Observability foundation (do first — makes the rest verifiable)

- [x] 1.1 Define a `ScanDiagnostic` record (locations considered, locations found, total parsed keys, matched count, unmatched count, sample unmatched keys, error notes) in the achievements service area.
- [x] 1.2 Extend `IAchievementUnlockSource` so reading unlocks can also yield the location/parse portion of the diagnostic (candidate paths considered, which existed, per-file parsed-key count, read/parse errors) without breaking the existing `ReadUnlocks` callers.
- [x] 1.3 Inject `ILogger<AchievementService>` and replace the silent `catch {}` blocks in the detection path (`StartWatching`, `OnSessionEndedAsync`, file read/parse in `SteamEmulatorUnlockSource.ReadUnlocks`) with logged warnings; leave behavior otherwise unchanged.
- [x] 1.4 Have `ScanUnlocksCoreAsync` assemble the full `ScanDiagnostic` (filling matched/unmatched against the stored definitions) and log it at information level on every scan.

## 2. Surface the diagnostic to the user

- [x] 2.1 Expose the last scan's `ScanDiagnostic` from `IAchievementService` (return it from the scan path per design decision 1, keeping existing callers working).
- [x] 2.2 In `GameDetailViewModel.ScanUnlocks`, when no new unlocks are found, set `AchievementStatus` to a concise diagnostic (locations searched, whether a recognized file was found, unmatched-key count) instead of only "No new unlocks found."

## 3. Broaden file-location coverage

- [x] 3.1 Replace the fixed path list in `SteamEmulatorUnlockSource.LocateFiles` with root+pattern rules over the known roots (existing `%AppData%`, `%PUBLIC%\Documents`, `%MyDocuments%`, game install folder) plus additional known locations (e.g. `Documents\OnlineFix\<appid>\…`, alternate stats/`SteamEmu` subfolders), still producing a concrete candidate-path list so watching is unchanged.
- [x] 3.2 Honor a Goldberg/`gbe_fork` `local_save.txt` redirect found in the game's `steam_settings` folder: resolve the redirected save root and add its appid-named achievement file(s) to the candidate list.
- [x] 3.3 Record every candidate location considered (and whether it existed) into the `ScanDiagnostic`.

## 4. Broaden format coverage

- [x] 4.1 Identify the formats currently returning empty (review `EmulatorAchievementParser`); add parsing for the additional emulator JSON/INI shapes found, keeping the "unrecognized → empty, never throw" contract.
- [x] 4.2 Ensure the parser surfaces a parse failure as a diagnostic note rather than a silent empty result, so an unreadable/unknown file is distinguishable from an absent one.

## 5. Robust key matching and unmatched-key handling

- [x] 5.1 Build the `byKey` lookup in `ScanUnlocksCoreAsync` with `StringComparer.OrdinalIgnoreCase`; add a last-resort normalized fallback (trim/prefix-strip) without forcing a fuzzy match.
- [x] 5.2 For a parsed unlock whose key matches no definition, increment the unmatched count and add it to the diagnostic's sample unmatched keys instead of silently `continue`-ing.

## 6. Bounded reconcile retry for the write-on-exit race

- [x] 6.1 In `OnSessionEndedAsync`, run the final reconcile as a short bounded retry (a few attempts over a couple of seconds), stopping early on any new unlock or an advancing file last-write time; ensure it runs on the background task and never blocks shutdown, relying on the existing per-game `_scanGates` to serialize against the live watcher.

## 7. Tests

- [x] 7.1 `EmulatorAchievementParserTests`: add cases for each newly-supported format and for the parse-failure-vs-empty distinction.
- [x] 7.2 `AchievementServiceTests`: case-insensitive key match marks the definition unlocked; an unmatched key is counted in the diagnostic and not lost.
- [x] 7.3 `AchievementServiceTests`: a recognized file in a non-default/redirected location is located and parsed (use the test `AppPaths`/temp roots).
- [x] 7.4 `AchievementServiceTests`: the session-end reconcile retry detects an unlock written just after the session ends.
- [x] 7.5 `AchievementServiceTests`: a scan with no unlocks produces a diagnostic whose summary explains why (no file found / unmatched count).
- [x] 7.6 Run `dotnet test`; confirm the full suite passes.

## 8. Verify end-to-end

- [x] 8.1 Build and run the app; for the reproducing game, open its detail view, click "Scan for unlocks", and confirm the status now reports a meaningful diagnostic (locations searched / file found / unmatched keys) — capture the running app per the project's verification preference. **Confirmed in-app** by the user's screenshot: *"No new unlocks. No recognized achievement file found (searched 65 known locations)…"* — the diagnostic surfaces correctly.

## 9. Reproducing game: Ooo (root cause found and fixed)

The reproducing game was **Ooo** (appid 2721890). Its emulator wrote unlocks to `<gameDir>\SteamData\user_stats.ini` (an ALI213/3DM "SteamData" wrapper) — a **location not in the candidate list** and a **format the parser didn't handle**: a flat `[ACHIEVEMENTS]` section of `"key" = {unlocked = true, time = N}` Lua-table lines, with **lowercase keys** (`area1`) vs the schema's uppercase (`AREA1`).

- [x] 9.1 Add `<gameDir>\SteamData\user_stats.ini` (+ `Achievements.ini`) to `SteamEmulatorUnlockSource.LocateFiles`.
- [x] 9.2 Parse the flat `[ACHIEVEMENTS]` Lua-table format in `EmulatorAchievementParser.ParseIni` (quoted keys, `unlocked = true`, `time = N`).
- [x] 9.3 `EmulatorAchievementParserTests.Ini_FlatAchievements_LuaTableValues_SteamDataStyle` covers it; full suite 138/138 green.
- [x] 9.4 Verified against the real file + DB: parser reads 13 keys; case-insensitive matching unlocks **all 12** of Ooo's schema achievements; `area7` correctly reported as 1 unmatched key.
- [x] 9.5 **Final user confirmation:** in the rebuilt app, open **Ooo → Achievements → Scan unlocks** and confirm the 12 achievements flip to unlocked. **Confirmed by the user — achievements detected correctly.**
