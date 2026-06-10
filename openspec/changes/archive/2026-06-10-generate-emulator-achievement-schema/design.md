## Context

`AchievementService.ScanUnlocksCoreAsync` reads emulator unlock files through `IAchievementUnlockSource` (today only `SteamEmulatorUnlockSource`) and returns a `ScanResult` carrying a `ScanDiagnostic`. The diagnostic's per-candidate record, `ScanCandidateInfo(Path, Existed, ParsedKeyCount, Error)`, captures only whether the **file** existed. When every candidate file is absent, `ScanDiagnostic.Summary` emits a single message — "No recognized achievement file found (searched N known locations)…" — regardless of whether the emulator's save folder is present.

Field investigation of *007 First Light* (gbe_fork): the save folder `%AppData%/GSE Saves/3768760/` exists and is actively written (game cloud-saves under `remote/`), but there is no `achievements.json` anywhere, because the crack's `steam_settings/` ships DLC config only — no `achievements.json` **schema**. gbe_fork only persists achievements it has a schema for, so the game's unlocks were dropped. Mosaic scans the right place; there is simply nothing to read.

Mosaic already resolves achievement definitions from the Steam Web API (`SteamWebApiClient.GetSchemaForGameAsync`) and stores them as `Achievement` rows. The gbe_fork schema file format was confirmed from the upstream example `post_build/steam_settings.EXAMPLE/achievements_EXAMPLE.json`: a JSON **array** of objects with fields `name`, `displayName`, `description`, `hidden` (string `"0"`/`"1"`), `icon`, `icongray`.

## Goals / Non-Goals

**Goals:**
- Make a "found nothing" scan explain whether the emulator save folder exists (schema missing) or not (unsupported/never-run), and point toward a fix.
- Let the user generate a correct gbe_fork/Goldberg `achievements.json` schema from Mosaic's resolved definitions and place it in the game's `steam_settings` folder, so future unlocks are tracked by the existing scan/watch path.
- Keep the operation safe: never write outside the game folder, never silently clobber a hand-made schema.

**Non-Goals:**
- Backfilling pre-schema unlocks (the emulator never recorded them).
- Placing achievement icon image files next to the schema (only `name` matters for unlock tracking; `icon`/`icongray` are written as empty strings).
- Supporting emulators other than the gbe_fork/Goldberg `steam_settings/achievements.json` layout in this change.
- Changing where the emulator stores unlock state or how it is parsed.

## Decisions

### 1. Track directory existence per candidate, not a single aggregate flag
Add `DirectoryExisted` to `ScanCandidateInfo`. `SteamEmulatorUnlockSource.ReadUnlocks` sets it via `Directory.Exists(Path.GetDirectoryName(file))` for every candidate (existing file or not). `ScanDiagnostic` gains `SaveFolderFound => Candidates.Any(c => c.DirectoryExisted)` and surfaces the first such folder for the message.

`ScanCandidateInfo` is a positional record; rather than break every construction site (including tests), add `DirectoryExisted` as an extra init-only member with a default, set via object-initializer at the one production call site (`new ScanCandidateInfo(file, existed, count, error) { DirectoryExisted = dirExisted }`). *Alternative considered:* a 5th positional parameter — rejected because it churns all existing call sites and tests for no readability gain.

`Summary` (no-file branch) becomes: if `SaveFolderFound` → "Found an emulator save folder (`<folder>`) but no achievements file in it — the emulator has no achievement schema, so it recorded no unlocks. Use ‘Generate emulator schema’."; else → "No emulator save folder found (searched N locations) — the game may not have run under a supported emulator, or uses one Mosaic doesn't recognize." The file-found branch is unchanged.

### 2. Schema generation lives in `AchievementService`, format/path in a small dedicated helper
`AchievementService` owns the Steam Web API client, settings, and DB context, so the orchestration (`GenerateEmulatorSchemaAsync(int gameId)`) belongs there: resolve definitions (reuse stored `Achievement` rows; if none and a key exists, run the existing refresh first), then serialize and write. The **format + target path** is gbe_fork/Goldberg-specific knowledge that already lives conceptually with `SteamEmulatorUnlockSource`; put a pure helper there (e.g. `SteamEmulatorUnlockSource.SchemaTargetPath(game)` returning `<gameDir>/steam_settings/achievements.json`, and a `BuildSchemaJson(defs)` serializer) so reading and writing the emulator layout stay co-located and unit-testable without I/O.

Return a small result record (e.g. `SchemaWriteResult { Written: bool, Path, Note }`) so the view model can report outcome on the existing `AchievementStatus` line. *Alternative considered:* extending `IAchievementUnlockSource` with a `WriteSchema` method — rejected: the interface is about *reading* unlock state, and only one source supports writing; a focused helper avoids widening the seam for every future read-only source.

### 3. Serialization uses the confirmed gbe_fork field shape
Emit a JSON array; each element: `name` = `ApiName`, `displayName` = `DisplayName`, `description` = `Description ?? ""`, `hidden` = `Hidden ? "1" : "0"` (**string**, not bool/number), `icon` = `""`, `icongray` = `""`. Use `System.Text.Json` with indentation and `UnsafeRelaxedJsonEscaping` so non-ASCII display names aren't `\uXXXX`-escaped. The `hidden`-as-string detail is the easy thing to get wrong, so it is asserted by a unit test.

### 4. Confirmation + safety guards
Preconditions checked before writing: game linked (`SteamAppId > 0`), a resolvable definition set (stored rows, or a Steam Web API key to fetch), the game folder resolvable from `ExecutablePath`. If the target file exists, the **view model** asks via `IDialogService.Confirm` before calling through with an overwrite flag (the service does not prompt; it stays UI-agnostic, matching the existing `Remove`/`FindAppId` pattern). Writes are constrained to the game folder; the service refuses a target it cannot resolve to a directory under the executable.

### 5. UI affordance reuses existing busy-gating
A new `GenerateSchemaCommand` + button in the Achievements expander, added to the `IsAchievementBusy` `NotifyCanExecuteChangedFor` set and `CanRunAchievementAction` guard, so it can't overlap a scan/refresh. Status/outcome goes to `AchievementStatus`. The button is most useful right after the improved diagnostic tells the user the schema is missing.

## Risks / Trade-offs

- **Generated schema doesn't match what the game expects (wrong/zero achievements detected later).** → Definitions come from the same Steam appid the user linked and that the scan matches against; format verified against the upstream example. Mismatched keys already surface as "unmatched" in the (now sharper) diagnostic.
- **Overwriting a user's hand-crafted `achievements.json`.** → Never overwrite without explicit confirmation; default action is non-destructive when the file is absent.
- **Writing into a game folder Mosaic doesn't own / permission errors.** → Confined to `<gameDir>/steam_settings`; best-effort with a clear failure note (e.g. file locked / access denied), never throwing into the UI.
- **User expects past achievements to appear.** → Explicitly documented in the proposal and surfaced in the success message ("future unlocks will be tracked; earlier ones aren't backfilled — re-earn or mark them manually").
- **Steam returns no achievements for the appid.** → Treated like "no definitions": no file written, message explains why (wrong App ID or the game genuinely has none).
