## Why

Automatic achievement detection silently failed to register an unlock for a **correctly-configured** game: a Steam-emulator (cracked) build, linked to its Steam appid, schema resolved (the achievement list displays in the detail view), and a Steam Web API key set — yet after the play session ended (which triggers the end-of-session reconcile scan that re-reads the files fresh), the newly-unlocked achievement never appeared.

Because every step of the detection pipeline swallows its own failures with bare `catch {}` and there is **no logging anywhere**, there is no way to tell which link in the chain broke. The two most probable causes — the emulator wrote its achievements file to a path that `SteamEmulatorUnlockSource`'s hard-coded location list does not cover, or in a format / key-casing that `EmulatorAchievementParser` / the matcher does not handle — are exactly the failure modes the current code makes invisible. The fix is to make detection observable first, then close the coverage and robustness gaps that observability exposes.

## What Changes

- **Make detection observable.** Add structured logging across the detection pipeline (which candidate file locations were checked, which existed, what each parsed to, how many parsed keys matched the resolved schema, and how many were skipped as unmatched) and replace silent `catch {}` swallowing with logged warnings. Surface a concise diagnostic to the user from the existing "Scan for unlocks" action instead of only "No new unlocks found."
- **Broaden emulator file-location coverage.** Extend `SteamEmulatorUnlockSource.LocateFiles` beyond today's fixed set to cover commonly-used emulator/crack save locations that are currently missed (e.g. Goldberg/`gbe_fork` `local_save.txt` redirects into the game folder, online-fix's `Documents\OnlineFix\<appid>\…`, additional CODEX/RUNE/ALI213/SmartSteamEmu and per-user roots), preferring a search of likely roots over only exact pre-listed paths.
- **Broaden format coverage and harden key matching.** Handle additional emulator file shapes the parser currently returns empty for, and match parsed achievement keys to schema `ApiName`s **case-insensitively** (with a normalized fallback) so a casing difference no longer silently drops an unlock.
- **Stop losing detected unlocks.** A parsed unlock whose key has no resolved schema definition is currently discarded silently; it SHALL instead be logged and counted (and, where the schema is resolvable, prompt a re-resolve) so a schema/key gap is visible rather than invisible.
- **Harden the end-of-session reconcile against a write-on-exit race.** Emulators that flush their achievements file as the game exits can write it just after the reconcile scan reads it; add a brief bounded retry so a shutdown-time unlock is not lost.

Out of scope (unchanged): detection remains best-effort and **Steam-emulator-file scoped** — real Steam-client achievements and other stores (GOG, Epic, Xbox, RetroAchievements) are not addressed here.

## Capabilities

### New Capabilities
<!-- none: this is a reliability/observability fix to existing behavior -->

### Modified Capabilities
- `achievements`: strengthen the **"Detect achievement unlocks from local Steam-emulator files"** and **"Notify achievement unlocks during a play session"** requirements (broader location and format coverage, case-insensitive key matching, a bounded session-end reconcile retry), and add an **observability** requirement that a detection scan records and exposes a diagnostic of what was searched, found, parsed, matched, and skipped.
- `library-ui`: extend the detail-view achievement requirement so that when a manual scan finds no unlocks, the view SHALL surface a brief diagnostic of why (locations searched, whether a recognized file was found, unmatched-key count) rather than only reporting "No new unlocks found."

## Impact

- **Code**: `Services/SteamEmulatorUnlockSource.cs` (location + read coverage, diagnostic capture), `Services/EmulatorAchievementParser.cs` (additional formats), `Services/AchievementService.cs` (logging, case-insensitive matching, unmatched-key handling, reconcile retry, diagnostic result), `Services/IAchievementService.cs` (scan returns/exposes a diagnostic), `ViewModels/GameDetailViewModel.cs` (surface the scan diagnostic). Possibly `App.xaml.cs` if a persistent log sink is added.
- **Logging/observability**: introduce `ILogger` usage in the achievement services (the host already configures logging); optionally a lightweight file log sink under `%LOCALAPPDATA%\Mosaic` so a user can share a detection trace.
- **Tests**: extend `EmulatorAchievementParserTests` and `AchievementServiceTests` for the new formats, case-insensitive matching, new candidate locations, the unmatched-key path, and the reconcile retry.
- **No breaking changes and no database migration** — the change is behavioral and additive. Per-game data, schema, and the manual path are untouched.
