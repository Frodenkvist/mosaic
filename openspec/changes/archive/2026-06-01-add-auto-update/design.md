## Context

Mosaic is a per-user, self-contained .NET 10 WPF app installed via an Inno Setup wizard (`installer\Mosaic.iss`, built by `installer\package.ps1`). The install layout was chosen with auto-update in mind:

- **Per-user install** to `%LocalAppData%\Programs\Mosaic` with `PrivilegesRequired=lowest` — files can be replaced **without elevation**.
- **Stable `AppId` GUID** + increasing `VersionInfoVersion` — running a newer `MosaicSetup-<version>.exe` upgrades in place.
- **Inno Setup silent mode** (`/SILENT`, `/VERYSILENT`) — the installer already special-cases silent uninstall (never prompts, keeps data) and a comment notes it is "what a future auto-updater will invoke."
- **User data is separate** (`%LocalAppData%\Mosaic`) and untouched by upgrades.

The product version is the single source of truth in `Mosaic.csproj` `<Version>`, surfaced to the running app as the assembly/file version and to the installer via `/DMyAppVersion`. Distribution is assumed to be **GitHub Releases** (`Frodenkvist/mosaic`), the natural feed for an updater. The installer is currently **unsigned**.

This change adds the *consumer* side — an in-app updater — and the small installer/packaging affordances it needs (silent-update relaunch, a published checksum). It deliberately **reuses the existing Inno Setup installer** rather than swapping in a different updater framework.

## Goals / Non-Goals

**Goals:**
- Let an installed Mosaic discover a newer published version, tell the user, and apply it in place with one click, no elevation.
- Be fully best-effort and non-blocking: a missing network, missing release, or rate-limit never degrades normal app use.
- Run **only** for genuinely installed builds; be an explicit no-op in dev (`dotnet run`) and for portable/unpacked copies.
- Verify the downloaded installer's integrity before running it (SHA-256), since builds are unsigned.
- Keep the user in control: an automatic-check toggle, an explicit "Check for updates" button, and an opt-in "Update now" (never silently restart).

**Non-Goals:**
- **Delta/differential** downloads — the updater fetches the full `MosaicSetup-<version>.exe` (~150 MB). Acceptable for an occasional desktop-app update.
- **Release channels** (beta/stable), staged rollouts, or rollback of an applied update.
- **Authenticode/code signing** of the installer or the update — still out of scope; SmartScreen caveat is unchanged. Integrity is via checksum only for now.
- **Replacing the installer toolchain** (e.g. with Velopack/Squirrel) — see Decision 1.
- **CI automation** of release publishing — the packaging script will emit the checksum, but wiring GitHub Releases publishing into CI is separate.

## Decisions

### Decision 1: Reuse the Inno Setup installer for applying updates (don't adopt Velopack)
The updater downloads the standard `MosaicSetup-<version>.exe` and runs it **silently** to perform the same in-place upgrade a user would do manually.

- **Why:** The installer already does everything an update needs — stable `AppId` in-place upgrade, per-user (no elevation), silent mode, `CloseApplications` to release locked files, and data preservation. Reusing it means **one** install/upgrade code path to maintain and test, and no second packaging format. The `.iss` was explicitly written anticipating this.
- **Alternatives considered:**
  - *Velopack/Squirrel* — gives delta updates and a built-in update API "for free," but it would **replace** the just-shipped wizard installer with a different (silent, Squirrel-style) install/uninstall model, invalidating the `installer` spec we just established. Rejected for this change; the per-user layout keeps it possible later if full-installer downloads become a real pain point.
  - *In-place file copy by the app itself* (download a zip, swap files) — fragile (the running process locks its own assemblies; no atomic swap; bypasses Apps & features version registration). Rejected.

### Decision 2: GitHub Releases as the update feed, consumed via the public REST API
Check `GET https://api.github.com/repos/Frodenkvist/mosaic/releases/latest`; read the release tag as the latest version and locate the `MosaicSetup-*.exe` asset and its checksum.

- **Why:** Releases are already the assumed distribution channel. The public REST API needs **no API key** (unauthenticated, HTTPS, ~60 req/hr/IP — far above one-check-per-day). `tag_name` gives the version; `assets[].browser_download_url` gives the installer and the checksum file. A `User-Agent` header is required by GitHub and will be set on the injected `HttpClient`.
- **Version comparison:** parse the tag (strip a leading `v`) into a `System.Version` and compare against the running assembly version (`Assembly.GetEntryAssembly().GetName().Version`). Only a strictly-greater remote version is an update. Non-parseable tags are ignored (treated as "no update").
- **Alternatives considered:** a hand-rolled `latest.json` on a static host (more infra to maintain, no benefit over the Releases API); the GitHub Atom releases feed (no asset metadata). Rejected.

### Decision 3: Integrity via a published SHA-256 checksum, verified before launch
`package.ps1` writes `MosaicSetup-<version>.exe.sha256` next to the installer; both are uploaded to the release. After download the updater computes SHA-256 and **aborts if it doesn't match**, deleting the file.

- **Why:** Builds are unsigned, so the updater must not execute an installer it can't vouch for. HTTPS already protects transport; the checksum additionally guards against truncated/corrupted downloads and a tampered asset, and gives a clean place to add Authenticode verification later. The checksum is fetched from the same release over HTTPS.
- **Trade-off:** This is integrity, not full authenticity (an attacker who can publish to the release could publish a matching checksum). Real authenticity arrives with code signing — explicitly deferred. Documented as a known limitation.

### Decision 4: Gate everything on "is this an installed build?"
The updater performs no network calls and offers no UI actions unless it detects an installed layout. Detection: the Inno Setup uninstaller (`unins000.exe`) exists in the executable's own directory.

- **Why:** Auto-update only makes sense for the installed app; `dotnet run` and portable copies must not try to download/run an installer over a dev tree. Inno Setup always drops `unins000.exe` into the install dir, so its presence beside `Mosaic.exe` is a reliable, dependency-free "installed" marker. (The install path `%LocalAppData%\Programs\Mosaic` is the default but user-changeable, so the uninstaller's presence is preferred over a hard-coded path check.)
- **Consequence:** In dev, "Check for updates" reports "updates are managed by the installer / not an installed build" rather than acting.

### Decision 5: Apply flow — download, verify, launch silently, exit, relaunch
On "Update now": download to a temp file under `%LocalAppData%\Mosaic` (or `%TEMP%`), verify checksum, then start the installer with silent flags and a custom relaunch flag, and shut Mosaic down so files aren't locked:

```
MosaicSetup-<v>.exe /SILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTMOSAIC
```

- The running app starts the installer process **detached**, then calls `Application.Shutdown()` so `CloseApplications` doesn't have to force-kill it.
- The installer upgrades in place. A new `[Run]` entry runs `Mosaic.exe` when the `/RESTARTMOSAIC` flag was passed, **including in silent mode** (gated by a `Check:` function), so the user lands back in the updated app. The existing finish-page launch entry keeps `skipifsilent` for normal interactive installs.
- **Why `/SILENT` (progress bar) over `/VERYSILENT`:** a brief progress window reassures the user something is happening during the ~150 MB upgrade; either works. Final choice left to implementation.
- **Why relaunch via the installer, not a helper exe:** avoids shipping a second executable; the installer is already the process that outlives Mosaic during the swap.

### Decision 6: Background check on startup, plus on-demand; throttled and preference-gated
`App.OnStartup` kicks off a best-effort `UpdateService.CheckForUpdateAsync()` on a background task **after** the main window is shown (mirroring the artwork fetch pattern), only if `AutomaticUpdatesEnabled` and the last check was over ~24h ago (`LastUpdateCheckUtc`). The Settings page always offers a manual, throttle-bypassing "Check for updates."

- **Threading:** `UpdateService` raises `UpdateAvailable` from a background thread; view models marshal to the UI with `App.RunOnUiAsync(...)`, exactly like `SessionStarted`/`ArtworkUpdated`. The "update available" prompt uses `IDialogService`.
- **Settings:** add `AutomaticUpdatesEnabled` (default `true`) and `LastUpdateCheckUtc` (nullable) to `AppSettings`; both persist via `SettingsService.SaveAsync()`. Surfaced in `SettingsView` alongside the existing API-key fields, plus a read-only current-version label.
- **DI:** register `IUpdateService → UpdateService` as a singleton in `App.ConfigureServices`; give it an `HttpClient` via `AddHttpClient<UpdateService>(...)` (User-Agent + timeout), matching `SteamGridDbClient`/`SteamWebApiClient`.

## Risks / Trade-offs

- **[Unsigned installer — checksum is integrity, not authenticity]** → Document clearly; verify SHA-256 before executing; structure verification so Authenticode signature checking can be added when signing lands. Do not auto-apply without user consent.
- **[GitHub API rate limiting / outage]** → Unauthenticated 60/hr/IP is ample for ≤1 check/day; all failures are swallowed (best-effort) and surfaced only as "couldn't check for updates" on a manual check. No retry storms.
- **[Self-update while the app is running locks files]** → App shuts itself down before/just after launching the installer; Inno Setup `CloseApplications` is the backstop. The detached installer process is not a child whose lifetime is tied to Mosaic.
- **[Relaunch-after-silent-update misfires]** → Gated strictly on the custom `/RESTARTMOSAIC` flag via an Inno `Check:`; interactive installs are unaffected (still `skipifsilent`). Manually testable by running the installer with the flag.
- **[Running update against a non-installed/dev build]** → Hard gate on `unins000.exe` presence; no download or launch otherwise.
- **[Failed/partial download or interrupted upgrade]** → Verify checksum and delete on mismatch; never launch an unverified file. An interrupted in-place upgrade is recoverable by re-running the installer; user data is never touched. The currently installed version remains runnable until the new installer completes.
- **[Version tag format drift]** → Parse defensively (`v` prefix stripped, `System.Version.TryParse`); unparseable → treat as no update rather than throwing.
- **[Download size (~150 MB) each update]** → Accepted (Non-Goal: delta updates). Only downloaded when the user clicks "Update now," not on a check.

## Migration Plan

Additive; no runtime data or schema changes.

1. Add `AutomaticUpdatesEnabled` / `LastUpdateCheckUtc` to `AppSettings` (back-compat: absent in old `settings.json` → defaults; `AutomaticUpdatesEnabled` defaults true).
2. Add `IUpdateService`/`UpdateService` + DI registration + startup background check.
3. Add Settings UI (version label, check button + status, auto-check toggle) and the update-available prompt.
4. Add the `[Run]` relaunch-on-`/RESTARTMOSAIC` entry to `Mosaic.iss`; have `package.ps1` emit `MosaicSetup-<version>.exe.sha256`; document the GitHub Releases publishing step (asset + checksum) in `installer\README.md` and `CLAUDE.md`.
5. Smoke test: build vN and vN+1 installers; install vN; from the app, check → notified → update now → verify it downloads, verifies, upgrades to vN+1, relaunches, and the library is intact.
- **Rollback:** the feature is inert without published releases/checksums; disabling is a settings toggle. Reverting the change removes the service and installer `[Run]` entry with no data impact.

## Open Questions

- **Check cadence / throttle window** — 24h assumed; confirm (could be a setting later).
- **`/SILENT` vs `/VERYSILENT`** for the apply step — progress bar vs fully hidden; pick during implementation/smoke test.
- **Temp download location** — `%LocalAppData%\Mosaic` (self-cleaning, same volume as install) vs `%TEMP%`; either fine, lean to the data dir for predictable cleanup.
- **Pre-releases** — `releases/latest` excludes pre-releases by default (desired); revisit if a beta channel is ever added.
- **Release publishing automation** — manual `gh release create` for now vs CI; out of scope but needed for the feature to do anything.
