## 1. Diagnostic: distinguish "save folder missing" from "achievements file missing"

- [x] 1.1 Add `bool DirectoryExisted` (init-only, default `false`) to `ScanCandidateInfo` in `Services/AchievementDiagnostics.cs` without breaking its positional constructor.
- [x] 1.2 In `SteamEmulatorUnlockSource.ReadUnlocks` (`Services/SteamEmulatorUnlockSource.cs`), compute `Directory.Exists(Path.GetDirectoryName(file))` for every candidate and set `DirectoryExisted` on each `ScanCandidateInfo`.
- [x] 1.3 Add `SaveFolderFound` (and `FoundSaveFolder`, the representative existing emulator folder excluding the game's own install folder) to `ScanDiagnostic`, and rewrite the no-file branch of `Summary` to emit the "save folder found, no achievements file → generate schema" message vs. the "no save folder found" message; leave the file-found branch unchanged.
- [x] 1.4 Unit-test the three `Summary` branches in `Tests/Mosaic.Tests` (save-folder-present-no-file, no-folder, and the existing file-found path), asserting the distinguishing wording.

## 2. Schema generation: format + target path helper

- [x] 2.1 In `Services/SteamEmulatorUnlockSource.cs`, add a pure `SchemaTargetPath(Game)` returning `<gameDir>/steam_settings/achievements.json` (null when the game folder can't be resolved) and a `BuildSchemaJson(IEnumerable<Achievement>)` serializer producing the gbe_fork array.
- [x] 2.2 Serialize each entry with exactly `name`, `displayName`, `description`, `hidden` (string `"0"`/`"1"`), `icon` (""), `icongray` ("") using `System.Text.Json` with indentation and relaxed (non-ASCII-safe) escaping.
- [x] 2.3 Unit-test `BuildSchemaJson`: exact field names present, `hidden` serialized as the string `"0"`/`"1"`, a non-ASCII display name round-trips unescaped, and the root is a JSON array.

## 3. Service operation

- [x] 3.1 Define a `SchemaWriteResult` record (`Written`, `Path`, `RequiresOverwriteConfirmation`, `Note`) and add `Task<SchemaWriteResult> GenerateEmulatorSchemaAsync(int gameId, bool overwrite = false, CancellationToken ct = default)` to `IAchievementService`.
- [x] 3.2 Implement it in `AchievementService`: load the game; guard not-linked / no game folder; gather definitions from stored `Achievement` rows, and when none exist but a Steam Web API key is set, run the existing refresh first; if still none, return a non-written result explaining why.
- [x] 3.3 Resolve the target via `SchemaTargetPath`; if the file exists and `overwrite` is false, return a result signalling "exists, confirmation needed" without writing; otherwise create `steam_settings` if needed and write the JSON (confined to the game folder). Catch IO/access errors and return them as a note rather than throwing.
- [x] 3.4 Unit-test `GenerateEmulatorSchemaAsync` against a temp game folder: writes the file for a linked game with definitions; refuses without definitions; does not overwrite an existing file unless `overwrite` is true.

## 4. UI affordance

- [x] 4.1 Add a `GenerateSchemaCommand` to `GameDetailViewModel` gated by `CanRunAchievementAction`, registered in the `IsAchievementBusy` `NotifyCanExecuteChangedFor` set; on first call (no overwrite) prompt via `IDialogService.Confirm` when the result signals an existing file, then re-invoke with `overwrite: true`.
- [x] 4.2 Report the outcome on `AchievementStatus`, including the success caveat that only future unlocks are tracked (earlier ones aren't backfilled).
- [x] 4.3 Add a "Generate emulator schema" button to the Achievements expander in `Views/GameDetailWindow.xaml`, bound to the command, with a tooltip explaining it writes `steam_settings/achievements.json` for the emulator.

## 5. Verify

- [x] 5.1 `dotnet build Mosaic.sln` and `dotnet test` pass (155 tests, 6 new).
- [x] 5.2 Manually confirm against *007 First Light*: scan now reports the save-folder-without-file case; Generate writes `F:\Games\007 First Light\Retail\steam_settings\achievements.json`; re-running prompts before overwrite. *(Confirmed working by the user.)*
