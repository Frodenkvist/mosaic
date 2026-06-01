## Why

Mosaic now ships as an installed Windows app (`MosaicSetup-<version>.exe`), but once installed there is no way for it to update itself — a user must manually find, download, and re-run a newer installer to get fixes and features. The installer was deliberately built per-user (no elevation) with a stable `AppId` and silent-install support precisely so the app could update itself later. This change delivers that: Mosaic checks for new versions, tells the user, and applies the update in place with one click.

## What Changes

- Add an in-app **update service** that checks the project's **GitHub Releases** for a newer published version than the running build, on a best-effort background basis at startup and on demand.
- Add a **Settings** affordance: the current installed version, a **"Check for updates"** button with status, and a toggle to enable/disable automatic background checks.
- When a newer version exists, show a **non-intrusive notification** offering "Update now" or "Later"; "Update now" **downloads** the new `MosaicSetup-<version>.exe`, **verifies** it against a published SHA-256 checksum, then **launches it silently** to upgrade in place and **relaunches** Mosaic.
- **Only run for installed builds** — update checks are a no-op when running from source (`dotnet run`) or any non-installed copy (detected via the Inno Setup uninstaller next to the executable).
- Persist update preferences in `settings.json` (`AppSettings`): automatic-check toggle and last-check timestamp (to throttle checks).
- Update the **installer** so a silent in-place upgrade works unattended and the app is **relaunched after a silent update**; have the **packaging/release process publish a checksum** for each installer asset so downloads can be verified.

Out of scope (future, not precluded): delta/differential updates, release channels (beta/stable), staged rollouts, and Authenticode signing of the installer/update (still unsigned — SmartScreen caveat unchanged).

## Capabilities

### New Capabilities
- `auto-update`: the installed app checking a release source for a newer version, gating checks to installed builds, surfacing an update notification and an on-demand check in the UI, respecting a user preference, and downloading + integrity-verifying + applying an update in place and relaunching.

### Modified Capabilities
- `installer`: add requirements for an **unattended silent in-place upgrade** suitable for invocation by the updater, **relaunching the app after a silent update**, and **publishing a verifiable checksum** alongside the installer asset.

## Impact

- **New service** under `Services\`: `IUpdateService` / `UpdateService` (uses an injected `HttpClient` via `AddHttpClient`, reads/writes settings, raises an "update available" event marshaled to the UI like the existing tracker/artwork events). Registered as a singleton in `App.ConfigureServices`; a best-effort check kicked off from `App.OnStartup` after the window shows.
- **`App.xaml.cs`**: register the service and start the background check (non-blocking, like artwork fetch).
- **`Services\AppSettings.cs`**: add `AutomaticUpdatesEnabled` (default true) and `LastUpdateCheckUtc`.
- **`SettingsViewModel` / `SettingsView.xaml`**: current-version display, "Check for updates" command + status, automatic-check toggle. A lightweight "update available" prompt via `IDialogService`.
- **`installer\Mosaic.iss`**: support relaunch after a silent update (a `[Run]` entry gated on a custom flag the updater passes).
- **`installer\package.ps1`** (and `installer\README.md` / `CLAUDE.md`): emit a SHA-256 checksum for `MosaicSetup-<version>.exe`; document the GitHub Releases publishing step the updater consumes.
- **Distribution**: relies on releases being published to GitHub Releases (`Frodenkvist/mosaic`) with the installer asset and its checksum. No new runtime dependency; update checks use the public GitHub Releases API over HTTPS (no key required).
- **No change** to the data directory or persistence; user data under `%LocalAppData%\Mosaic` is untouched by updates (the in-place upgrade preserves it, as today).
