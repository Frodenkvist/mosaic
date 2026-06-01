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
4. emits `installer\dist\MosaicSetup-<version>.exe`.

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
- Auto-update is not yet implemented; the per-user layout and stable `AppId` are chosen to
  support it later.
