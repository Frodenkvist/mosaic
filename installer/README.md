# Mosaic installer

Builds the Windows installer for Mosaic: a wizard-driven `MosaicSetup-<version>.exe` plus a
standalone uninstaller, packaging a self-contained (.NET 10 bundled) win-x64 build.

## Prerequisites

- **.NET 10 SDK** (`dotnet`) — to publish the app.
- **Inno Setup 6** — provides the `ISCC.exe` compiler. Install with:
  ```powershell
  winget install --exact --id JRSoftware.InnoSetup --accept-package-agreements --accept-source-agreements
  ```
  or download from <https://jrsoftware.org/isdl.php>.

## Build

From the repo root:

```powershell
.\installer\package.ps1            # version comes from <Version> in Mosaic.csproj
.\installer\package.ps1 -Version 1.2.0   # or override explicitly
```

This:
1. cleans `installer\publish\` and `installer\dist\`,
2. runs `dotnet publish -c Release -r win-x64 --self-contained true` into `installer\publish\`,
3. compiles `Mosaic.iss` with Inno Setup,
4. emits `installer\dist\MosaicSetup-<version>.exe`,
5. emits `installer\dist\MosaicSetup-<version>.exe.sha256` (the checksum the auto-updater verifies).

## Publishing a release (for auto-update)

> **Automated:** a push to `master` that bumps `<Version>` makes the Jenkins pipeline (`Jenkinsfile`)
> build, test, package, and publish the release automatically. See [`docs/ci.md`](../docs/ci.md).
> The manual steps below remain valid for a local/one-off release.

The in-app auto-updater reads the **latest GitHub Release** of `Frodenkvist/mosaic`. To publish a
version so installed clients can update to it:

1. Bump `<Version>` in `Mosaic.csproj` and build the installer (`.\installer\package.ps1`).
2. Create a GitHub Release whose **tag matches the version** (`v<version>`, e.g. `v1.2.0`).
3. Upload **both** assets from `installer\dist\`: `MosaicSetup-<version>.exe` **and** its
   `MosaicSetup-<version>.exe.sha256`. The updater requires the checksum and refuses to apply an
   update it can't verify.

```powershell
.\installer\package.ps1 -Version 1.2.0
gh release create v1.2.0 `
    installer\dist\MosaicSetup-1.2.0.exe `
    installer\dist\MosaicSetup-1.2.0.exe.sha256 `
    --title "Mosaic 1.2.0" --notes "..."
```

## Files

- `Mosaic.iss` — Inno Setup script (wizard pages, per-user install, shortcuts, uninstall data prompt).
- `package.ps1` — publish + compile orchestration.
- `publish\`, `dist\` — build output (git-ignored).

## Install behavior

- **Per-user, no admin** — installs to `%LocalAppData%\Programs\Mosaic` with no UAC prompt.
- **Shortcuts** — Start Menu always; desktop only if the user opts in.
- **Apps & features** — registered with name/version/publisher; standalone uninstaller at
  `%LocalAppData%\Programs\Mosaic\unins000.exe`.
- **Upgrade** — installing a newer version replaces the old one in place (stable `AppId`).
- **User data** — the game library, artwork, and settings live under `%LocalAppData%\Mosaic`
  (separate from the program files). Uninstall keeps this by default; it asks whether to also
  delete it (defaulting to keep).

## Notes

- The installer is currently **unsigned**, so Windows SmartScreen may warn about an unknown
  publisher. Code signing is a planned follow-up (add a `SignTool` step to `Mosaic.iss`).
- **Auto-update** is implemented (see `Services\UpdateService.cs`). An installed Mosaic checks the
  latest GitHub Release in the background (and on demand from Settings), and on the user's consent
  downloads the new `MosaicSetup-<version>.exe`, **verifies it against the published `.sha256`**, then
  runs it **silently** (`/SILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTMOSAIC`) to upgrade in place and
  relaunch. The `[Code]`/`[Run]` `WantRestartMosaic` gate in `Mosaic.iss` performs the relaunch only
  when the updater passes `/RESTARTMOSAIC`. Update behavior is gated to **installed builds** (detected
  via `unins000.exe` next to the executable); a `dotnet run` build never self-updates.
- The checksum provides **integrity** (guards against corrupted/tampered downloads), not full
  **authenticity** — that requires code signing, which is still the planned follow-up.
