## Context

Automatic achievement detection (`AchievementService` + `SteamEmulatorUnlockSource` + `EmulatorAchievementParser`) reads the achievement files written by Steam emulators and marks the matching schema definitions unlocked. It is wired correctly: `AchievementService` is constructed at startup (via `MainViewModel`) and subscribes to `IPlayTracker.SessionStarted/SessionEnded`, watching files live during a session and running a reconcile scan on session end. New games default to `AchievementTrackingEnabled = true` and `AchievementSource.Auto`.

Despite this, a real unlock went undetected for a game that was a Steam-emulator build, linked to its appid, with the schema resolved (the detail view lists achievements) and a Steam Web API key configured — i.e. the entire prerequisite chain held, and the failure survived the end-of-session reconcile scan. The detection step itself did not produce the unlock.

The pipeline is effectively undebuggable in its current form:
- `SteamEmulatorUnlockSource.LocateFiles` enumerates a **fixed, hard-coded** list of exact paths. Any emulator/crack that saves outside that list (a `local_save.txt` redirect into the game folder, online-fix's `Documents\OnlineFix\…`, an alternate per-user root, a differently-named stats folder) is invisible.
- `EmulatorAchievementParser` recognizes Goldberg/GSE JSON and CODEX/ALI213-style INI; anything else parses to empty.
- `ScanUnlocksCoreAsync` matches parsed keys to definitions with `StringComparer.Ordinal` (case-sensitive) and **silently `continue`s** past any key with no matching definition.
- Every failure path is a bare `catch {}`. There is **no logging anywhere** in the achievement services, so "found nothing" and "errored while reading" are indistinguishable, and the user is shown only "No new unlocks found."

This change is observability-first: make the scan explain itself, then close the coverage/robustness gaps that the explanation exposes.

## Goals / Non-Goals

**Goals:**
- A scan produces a structured diagnostic (locations considered, which existed, keys found, keys matched, keys unmatched, errors) that is logged and surfaced to the user via the existing "Scan for unlocks" status.
- Broaden file-location coverage from a fixed path list to a discovery strategy that also finds save-redirect and alternate-root locations.
- Match emulator keys to schema definitions case-insensitively, with a normalized fallback.
- Stop silently discarding a detected unlock whose key has no definition — count it and make it visible.
- Tolerate the write-on-exit race with a brief bounded retry in the session-end reconcile.
- Replace silent `catch {}` swallowing with logged warnings in the detection path.

**Non-Goals:**
- No new unlock sources. Detection stays **Steam-emulator-file scoped**; real Steam-client achievements and other stores (GOG, Epic, Xbox, RetroAchievements) remain out of scope.
- No change to play tracking, schema resolution, manual marking, icon caching, the monotonic-unlock guarantee, or the persistence model.
- No database schema/migration changes.
- No change to the established background-thread → `App.RunOnUiAsync` UI marshalling.

## Decisions

### 1. A `ScanDiagnostic` value carried back from the source through the service to the caller

`IAchievementUnlockSource.ReadUnlocks` currently returns only `IReadOnlyList<ParsedUnlock>`. Add a parallel path that also yields a diagnostic: the candidate locations considered, which existed, per-file parsed-key counts, and any read/parse errors. Concretely, introduce a small `record ScanDiagnostic` (locations considered/found, total parsed keys, matched count, unmatched count + sample unmatched keys, error notes) and have the source populate the location/parse portion while `AchievementService.ScanUnlocksCoreAsync` fills in the match/unmatched portion (only it knows the stored definitions).

`ScanUnlocksAsync` returns the newly-unlocked list today; keep that signature for existing callers and additionally expose the last diagnostic. Two viable shapes — (a) change the return to `(IReadOnlyList<Achievement> Newly, ScanDiagnostic Diagnostic)`, or (b) keep the return and add the diagnostic to the result via a new method/event. **Decision: (a)** — return a small result struct from a new internal overload and keep a thin public method that the view model calls for the diagnostic; the design favors an explicit return over event plumbing because the scan is request/response (the user clicked "Scan"). The live-watch path discards the diagnostic (it only logs).

*Alternative considered:* raise the diagnostic on an event like `AchievementsChanged`. Rejected: the manual scan is synchronous from the user's perspective and an event would race the `LoadAchievementsAsync` refresh in the view model.

### 2. Location discovery instead of a fixed path list

Replace the exact-path list in `LocateFiles` with a small set of **root + pattern** rules: for each known emulator root (the existing `%AppData%`, `%PUBLIC%\Documents`, `%MyDocuments%` roots, plus the game install folder and additions such as `Documents\OnlineFix`), look for the appid-named subfolder and the known file names, and additionally honor a Goldberg/`gbe_fork` `local_save.txt` redirect found in the game's `steam_settings` folder (which points the save root elsewhere). Keep enumeration shallow and bounded — resolve specific candidate paths under each root rather than recursively walking large trees — so detection stays cheap and predictable. The result is still a concrete candidate-path list (so watching is unchanged), just produced from rules plus a redirect check rather than a frozen literal list.

*Alternative considered:* a broad recursive search for any `achievements.json`/`.ini` under `%AppData%`/the game folder. Rejected: too slow, and prone to false matches from unrelated files; the diagnostic-driven approach lets us add specific known locations as they surface.

### 3. Case-insensitive key matching with a normalized fallback

Build the `byKey` lookup with `StringComparer.OrdinalIgnoreCase` instead of `Ordinal`. If a parsed key still doesn't match, attempt a normalized comparison (trim, strip a leading `ACH_`/`ACHIEVEMENT_`-style prefix only as a last-resort fallback) — but record any key that matches no definition as **unmatched** in the diagnostic rather than forcing a fuzzy match. Steam API names are nominally case-sensitive, but emulator files in the wild frequently differ only in case, and a false merge is far less likely than a casing mismatch given keys are already game-scoped.

*Alternative considered:* keep `Ordinal` and rely only on broader location coverage. Rejected: casing mismatches are a known emulator-file quirk and cost nothing to handle defensively.

### 4. Bounded reconcile retry for the write-on-exit race

`OnSessionEndedAsync` runs one final `ScanUnlocksAsync`. Some emulators flush the achievements file as the game process exits, which can land just after that scan reads it. Add a short bounded retry (e.g. a few attempts over a couple of seconds, stopping early once a scan reports any new unlock or the file's last-write time advances). This is best-effort and must never block app shutdown; it runs on the existing background task. The per-game `_scanGates` semaphore already serializes overlapping scans, so retries are safe against the live-watch path.

*Alternative considered:* a fixed single delay before the reconcile scan. Rejected: a fixed delay is both slower in the common case and less reliable than a short adaptive retry.

### 5. Logging via `ILogger`, optional file sink

Inject `ILogger<AchievementService>` (the host's `CreateDefaultBuilder` already registers logging). Log the diagnostic at information level on each scan and warnings on caught read/parse errors. Because a WPF app has no console, optionally add a lightweight file logging provider writing under `%LOCALAPPDATA%\Mosaic` so a user can share a detection trace; this is an additive, low-risk convenience and can be deferred if the in-app diagnostic proves sufficient. Tests can assert on diagnostics directly rather than on log output.

## Risks / Trade-offs

- **[Broader location discovery yields a false-positive file]** → Match is still gated on the appid-named folder and recognized file names/formats, and the parser ignores unrecognized content; the diagnostic surfaces exactly which file was read, so a wrong match is visible rather than silent.
- **[Case-insensitive / normalized matching merges two distinct achievements]** → Keys are per-game and the normalized fallback is last-resort and recorded; the realistic failure mode (casing) is the one being fixed, and unmatched keys are reported instead of force-matched.
- **[Reconcile retry delays nothing but could spin]** → Retry is bounded by attempt count and total time, stops early on success or an advancing file timestamp, runs on a background task, and never blocks shutdown.
- **[Optional file log writes inside the data dir]** → Reuses `AppPaths`/`IsInsideDataDirectory` conventions, is size-bounded/append-only, and is optional; if omitted, `ILogger` + the in-app diagnostic still satisfy the observability requirement.
- **[Root cause may be a location/format this change still doesn't cover]** → Acceptable and expected: the diagnostic is the durable win — it turns the next "didn't register" from invisible into a concrete report of what was searched and what was found, so coverage can be extended incrementally.

## Open Questions

- Should the optional file log sink ship in this change or be deferred once the in-app scan diagnostic is in place? (Leaning: ship `ILogger` usage now; gate the file sink on whether the in-app diagnostic is enough.)
- Should an unmatched-but-detected key trigger an automatic schema re-resolve (when a key is configured), or only be reported? (Leaning: report first; auto-refresh only if it proves to be a common cause.)
- Are there additional specific emulator save locations to seed the discovery rules with from the reproducing game, once its actual file path is identified during implementation?
