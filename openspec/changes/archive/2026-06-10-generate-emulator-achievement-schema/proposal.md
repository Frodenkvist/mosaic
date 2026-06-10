## Why

When a Steam-emulator crack ships a `steam_settings` folder **without** an achievements schema (`steam_settings/achievements.json`), gbe_fork/Goldberg only tracks achievements it has a schema for — so the game's `SetAchievement` calls are silently dropped and **no unlock file is ever written**. Mosaic scans the correct location (`%AppData%/GSE Saves/<appid>/achievements.json`) but finds nothing, and the current diagnostic ("No recognized achievement file found") can't tell the user *why*: it conflates "the emulator save folder exists but has no achievements file" (schema missing) with "no save folder at all" (game never ran under a supported emulator). This was the exact failure observed for *007 First Light*, and the user has no actionable next step.

## What Changes

- **Sharper scan diagnostic.** Track whether each candidate file's *parent directory* existed, not just the file, so a "found nothing" scan distinguishes three cases: (a) an emulator save folder exists but holds no achievements file → tell the user the emulator has no achievement schema and point them at the new generate action; (b) no save folder found at all → the game may not have run under a supported emulator yet, or uses one Mosaic doesn't recognize; (c) a file existed but parsed/matched poorly (today's behavior). Name the found folder when there is one.
- **Generate the emulator achievement schema.** A new action fetches the linked game's achievement definitions and writes a gbe_fork/Goldberg-format `steam_settings/achievements.json` next to the game executable, so the emulator will recognize and persist *future* unlocks. After generation, Mosaic's existing scan/watch path detects unlocks with no further change.
- **Detail-view affordance.** A new button in the Achievements expander, gated by the existing busy guard, that runs generation and reports outcome on the existing status line. Overwriting an existing schema file requires user confirmation (never clobber a hand-made schema).

Not in scope: backfilling achievements earned *before* the schema existed (the emulator never recorded them — the user re-earns or marks them manually); downloading/placing achievement icon images alongside the schema.

## Capabilities

### New Capabilities
<!-- None: both changes extend the existing achievements capability. -->

### Modified Capabilities
- `achievements`: Add a new requirement to **generate and place a Steam-emulator achievement schema** for a linked game (preconditions, target location, no-clobber behavior). Modify the existing **"Achievement detection is observable"** requirement so the scan diagnostic records per-candidate directory existence and distinguishes "save folder present but no achievements file" from "no save folder found."

## Impact

- **Spec**: `openspec/specs/achievements/spec.md` (one modified requirement, one added requirement).
- **Code**:
  - `Services/AchievementDiagnostics.cs` — add directory-existence to `ScanCandidateInfo`; extend `ScanDiagnostic.Summary` branches.
  - `Services/SteamEmulatorUnlockSource.cs` — populate directory existence in `ReadUnlocks`.
  - `Services/IAchievementService.cs` / `Services/AchievementService.cs` — new `GenerateEmulatorSchemaAsync` operation reusing `SteamWebApiClient` / stored definitions; serialize to the gbe_fork array format and write under the game folder.
  - `ViewModels/GameDetailViewModel.cs` + `Views/GameDetailWindow.xaml` — new command + button + confirmation.
- **Tests**: `Tests/Mosaic.Tests` — diagnostic Summary branches; schema serialization (exact field names, `hidden` as string `"0"/"1"`).
- **No data/schema migration, no new dependency.** Steam Web API key remains optional (generation reuses stored definitions when present, otherwise requires the key to fetch).
