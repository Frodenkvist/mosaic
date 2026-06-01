## 1. Settings & version plumbing

- [x] 1.1 Add `AutomaticUpdatesEnabled` (bool, default `true`) and `LastUpdateCheckUtc` (nullable `DateTime`) to `Services\AppSettings.cs`; confirm an old `settings.json` lacking them deserializes to the defaults
- [x] 1.2 Add a helper to read the running build version (`Assembly.GetEntryAssembly()?.GetName().Version`) and an "is installed build" check (the installer's `unins000.exe` exists alongside the executable)

## 2. Update service (core)

- [x] 2.1 Add `IUpdateService` (`Services\IUpdateService.cs`): `CheckForUpdateAsync(bool force)`, `DownloadAndApplyAsync(...)`, an `UpdateAvailable` event carrying the available version, and a property/result describing the latest known result
- [x] 2.2 Implement `UpdateService` (`Services\UpdateService.cs`) using an injected `HttpClient`: query `GET https://api.github.com/repos/Frodenkvist/mosaic/releases/latest` with a `User-Agent` header; parse the tag (strip leading `v`) into a `System.Version`; locate the `MosaicSetup-*.exe` asset and its `.sha256` asset
- [x] 2.3 Implement version comparison: report an update only when the remote version is strictly greater than the running version; treat unparseable tags / missing asset as "no update"
- [x] 2.4 Gate all behavior on the installed-build check (Decision 4): no network calls or apply actions for non-installed builds; the on-demand path returns a clear "not an installed build" result
- [x] 2.5 Apply automatic-check throttle: when `force` is false, skip if `LastUpdateCheckUtc` is within ~24h; update `LastUpdateCheckUtc` and persist via `ISettingsService.SaveAsync()` after a check
- [x] 2.6 Implement download to a temp file (under `%LocalAppData%\Mosaic`), compute SHA-256, compare to the published checksum, and abort + delete the file on mismatch (Decision 3 / integrity requirement)
- [x] 2.7 Implement apply + relaunch: start the verified installer detached with `/SILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTMOSAIC`, then `Application.Shutdown()` so files aren't locked (Decision 5)
- [x] 2.8 Make every operation best-effort: catch/log network, parsing, and I/O failures so they never block or crash the app; surface only as a result status
- [x] 2.9 Register `IUpdateService → UpdateService` as a singleton in `App.ConfigureServices` and configure its `HttpClient` via `AddHttpClient<UpdateService>(...)` (User-Agent + timeout)

## 3. App startup integration

- [x] 3.1 In `App.OnStartup`, after the main window is shown, kick off a non-blocking background automatic check (only when installed, enabled, and not throttled) — mirroring the best-effort artwork-fetch pattern
- [x] 3.2 Marshal the `UpdateAvailable` event to the UI thread via `App.RunOnUiAsync(...)` and route it to the notification (Section 4)

## 4. Settings UI & update notification

- [x] 4.1 In `SettingsViewModel`: expose the current version (read-only), an `AutomaticUpdatesEnabled` toggle bound to settings (saved on change), a `CheckForUpdatesCommand` (force = true), and a status string (checking / up to date / update available / failed / not an installed build)
- [x] 4.2 In `Views\SettingsView.xaml`: add a version label, the "Check for updates" button + status text, and the automatic-checks toggle, styled with the existing settings layout
- [x] 4.3 Add the "update available" prompt offering "Update now" / "Later" (via `IDialogService`); "Update now" invokes download+verify+apply, "Later" dismisses without downloading — implemented in `MainViewModel.PromptForUpdateAsync` (driven by the `UpdateAvailable` event from both the startup and on-demand checks)
- [x] 4.4 Surface verification/apply failures to the user as a clear message; never execute an unverified installer

## 5. Installer: relaunch after silent update

- [x] 5.1 In `installer\Mosaic.iss`, add a `[Code]` `Check:` function that returns true when the `/RESTARTMOSAIC` flag was passed on the command line
- [x] 5.2 Add a `[Run]` entry that launches `{app}\Mosaic.exe` (`nowait`) gated by that `Check:`, so a flagged silent upgrade relaunches the app; keep the existing finish-page launch entry `skipifsilent` for interactive installs

## 6. Packaging: checksum & release docs

- [x] 6.1 In `installer\package.ps1`, after the installer is produced, compute its SHA-256 and write `MosaicSetup-<version>.exe.sha256` next to it in `installer\dist\`
- [x] 6.2 Document the GitHub Releases publishing step (upload both the installer and its `.sha256` as release assets; tag matching the version) in `installer\README.md`

## 7. Verification (manual smoke test)

> 7.1 is automatable and was run here. 7.2–7.8 require interactive install/upgrade, observing the
> UAC behavior, and publishing a real GitHub Release; they must be performed manually on a Windows
> desktop and are left unchecked.

- [x] 7.1 Run `installer\package.ps1` and confirm both `MosaicSetup-<version>.exe` and its `.sha256` are produced and the checksum matches
- [ ] 7.2 Build version vN and vN+1 installers; install vN; confirm the running app reports "is installed build" and shows the correct current version in Settings
- [ ] 7.3 Publish vN+1 to a test release (or point at one); trigger "Check for updates" and confirm the update-available notification appears; choose "Later" and confirm nothing is downloaded
- [ ] 7.4 Choose "Update now"; confirm the installer downloads, the checksum verifies, the upgrade applies in place with no UAC prompt, and Mosaic relaunches on the new version
- [ ] 7.5 Tamper with / mismatch the checksum and confirm the update aborts, the file is deleted, and a clear "could not verify" message is shown
- [ ] 7.6 After an update, confirm the game library, artwork, and settings are intact
- [ ] 7.7 Run `dotnet run` (dev build) and confirm no automatic check fires and "Check for updates" reports "managed by the installer / not an installed build"
- [ ] 7.8 Disable automatic checks and confirm no background check runs on next startup; confirm the throttle prevents a second automatic check within a day

## 8. Documentation

- [x] 8.1 Update `installer\README.md` "Notes" and `CLAUDE.md` (Packaging / Release): describe the auto-update flow, the installed-build gate, checksum integrity (and that builds remain unsigned — integrity not authenticity), and the relaunch flag
