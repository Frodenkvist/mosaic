## Context

Mosaic is a self-contained-capable .NET 10 WPF app (`Mosaic.csproj` at the repo root, output `Mosaic.exe`). It is Windows-only by design and stores all runtime data under `%LocalAppData%\Mosaic` (resolved by `Services\AppPaths.cs`: `mosaic.db`, `settings.json`, `artwork\`). EF Core migrations are applied automatically on startup, so the installer does not need to provision or touch the database.

Today there is no distribution path: a user must clone the repo and run `dotnet run`. This change adds a packaging pipeline and a wizard installer/uninstaller. There are no existing build-tooling conventions to follow (no CI packaging, no version metadata in the csproj), so this design also establishes those.

A stated future goal is **auto-update**. It is out of scope here, but the install layout and metadata chosen now must not block it.

## Goals / Non-Goals

**Goals:**
- Produce a single distributable `MosaicSetup-<version>.exe` that installs Mosaic via a standard wizard, with no prerequisites on the target machine.
- Produce a working standalone uninstaller that removes the app cleanly and registers in Apps & features.
- Preserve the user's game library across uninstall/reinstall by default; allow opt-in data deletion.
- Make publish + packaging reproducible from a single documented command.
- Choose an install layout and versioning scheme compatible with a future auto-updater.

**Non-Goals:**
- Auto-update (checking for / downloading / applying new versions) — future change.
- Code signing / Authenticode certificates — noted as a risk, not implemented here (can be layered onto the same `.iss` later).
- Per-machine / all-users install, MSI/Group-Policy deployment, Microsoft Store / MSIX packaging.
- CI automation of releases (GitHub Actions). The packaging script is designed to be CI-callable, but wiring CI is not part of this change.
- ARM64 or x86 builds — win-x64 only.

## Decisions

### Decision 1: Inno Setup as the installer toolchain
Use Inno Setup (script `installer\Mosaic.iss`, compiled by `ISCC.exe`).

- **Why:** It produces exactly what was requested — a wizard-driven `setup.exe` plus an auto-generated standalone uninstaller (`unins000.exe`) placed in the install dir and registered in Apps & features. It is free, mature, scriptable, and supports per-user installs, in-place upgrades, custom uninstall logic (Pascal scripting for the data-deletion prompt), and silent mode (`/SILENT`, `/VERYSILENT`) — the last of which is exactly what a future auto-updater will invoke.
- **Alternatives considered:**
  - *WiX / MSI* — output is an `.msi`, not a wizard `.exe`, and there is no standalone `uninstall.exe`; heavier for a single-user app. Rejected: doesn't match the request.
  - *NSIS* — capable but more low-level scripting for equivalent wizard pages. Rejected: Inno Setup is closer out of the box.
  - *Velopack* — gives auto-update for free, but its install is a quick/silent Squirrel-style flow, not a classic multi-page wizard, and uninstall is via Apps & features rather than a standalone `.exe`. Rejected for *this* change because it conflicts with the explicit "wizard + uninstall exe" requirement; revisit when auto-update is built (the per-user layout chosen here keeps that door open).

### Decision 2: Self-contained, single-folder, win-x64 publish
Publish with `dotnet publish -c Release -r win-x64 --self-contained true`, output to `installer\publish\`.

- **Why:** The .NET 10 Desktop runtime is bundled, so the installer works on any Windows 10/11 x64 machine with zero prerequisites — the best end-user experience and a prerequisite for self-sufficient auto-update. A self-contained WPF app cannot be a true single *file* cleanly across all assemblies, so we ship the **publish folder** (the installer packs the whole directory). `PublishSingleFile` is not required.
- **Trade-off:** Installer is large (~150 MB) and the published output is the whole folder. Acceptable for a desktop game manager. `PublishTrimmed` is **not** used (WPF + reflection + EF Core make trimming risky).
- **Alternative:** Framework-dependent (tiny installer) — rejected; it would force the user to find and install the .NET 10 Desktop Runtime and complicate auto-update.

### Decision 3: Per-user install to `%LocalAppData%\Programs\Mosaic` (no admin)
`PrivilegesRequired=lowest`, `DefaultDirName={localappdata}\Programs\Mosaic`.

- **Why:** No UAC prompt; a regular user can install/upgrade/uninstall. Critically, a future auto-updater can replace files in a user-writable location **without elevation**. The program directory (`%LocalAppData%\Programs\Mosaic`) is deliberately distinct from the data directory (`%LocalAppData%\Mosaic`), so installing/uninstalling program files never collides with the user's library.
- **Alternative:** Per-machine to `Program Files` — requires admin for every install and update; rejected for a single-user app and incompatible with frictionless auto-update.

### Decision 4: User-data lives outside the install dir; uninstall asks before deleting it
`AppPaths` already stores data under `%LocalAppData%\Mosaic`, separate from the program files. The installer never creates or migrates data. The uninstaller, via an Inno Setup Pascal `[Code]` routine, shows a checkbox "Also delete my game library, artwork, and settings" that is **unchecked by default**; only when checked does it recursively delete `%LocalAppData%\Mosaic`.

- **Why:** Reinstall and (future) update flows must not destroy the library. Defaulting to keep is the safe choice; the explicit opt-in covers users who want a full removal.
- **Note:** Standard uninstall (data kept) removes only program files, shortcuts, and the ARP entry.

### Decision 5: Versioning via csproj metadata, surfaced to the installer
Add `Version`, `AssemblyVersion`, `FileVersion`, `Product`, `Company`, and `ApplicationIcon` (already present) to `Mosaic.csproj`. The packaging script reads the version (or it is passed as a parameter) and passes it to `ISCC.exe` via `/DMyAppVersion=...`, which drives the output filename `MosaicSetup-<version>.exe`, the ARP "version" field, and the in-place upgrade `AppId`/`VersionInfoVersion`.

- **Why:** Single source of truth for the version; an in-place upgrade is recognized by a stable `AppId` GUID with an increasing version, which is also the mechanism a future auto-updater relies on.

### Decision 6: Packaging entry point — `installer\package.ps1`
One PowerShell script: clean → `dotnet publish` (self-contained win-x64) → invoke `ISCC.exe` against `Mosaic.iss` with the version define → emit `MosaicSetup-<version>.exe` into `installer\dist\`. Document it in `CLAUDE.md`. The script locates `ISCC.exe` (default install path or `iscc` on PATH) and fails with a clear message if Inno Setup is not installed.

### Installer wizard flow (summary)
Welcome → Select install location (default `{localappdata}\Programs\Mosaic`, changeable) → Select additional tasks (create desktop shortcut, default off) → Ready to install → Installing → Finished (with "Launch Mosaic now" checkbox). Start Menu shortcut to `Mosaic.exe` is always created. The icon is taken from the bundled `Mosaic.ico`.

## Risks / Trade-offs

- **[No code signing → SmartScreen / AV warnings]** → Unsigned per-user installers trigger Windows SmartScreen "unknown publisher" warnings. Mitigation: document it; structure the `.iss` so a `SignTool` step / certificate can be added later without restructuring. Out of scope to obtain a cert now.
- **[Large installer (~150 MB) from self-contained publish]** → Mitigation: accepted as the cost of zero-prerequisite install; Inno Setup's LZMA2 compression reduces it substantially. Revisit only if size becomes a real distribution problem.
- **[Inno Setup is an extra build dependency not on the machine/CI]** → Mitigation: `package.ps1` detects a missing `ISCC.exe` and prints install guidance (`winget install JRSoftware.InnoSetup`); document the dependency in `CLAUDE.md`.
- **[Per-user install means PATH/`Program Files` expectations differ]** → Mitigation: shortcuts (Start Menu/desktop) are the supported launch path; no PATH entry is added. Documented.
- **[App running during upgrade/uninstall locks files]** → Mitigation: Inno Setup's `CloseApplications`/`RestartApplications` handling; the wizard will prompt to close Mosaic if `Mosaic.exe` is in use.
- **[Trimming/single-file pitfalls with WPF + EF Core]** → Mitigation: explicitly do **not** trim and do **not** force single-file; ship the publish folder.

## Migration Plan

This is additive build tooling; there is nothing to roll back in the app at runtime.
1. Add version/publish metadata to `Mosaic.csproj`.
2. Add `installer\Mosaic.iss` and `installer\package.ps1`.
3. Run `installer\package.ps1` locally to produce and smoke-test `MosaicSetup-<version>.exe` (install → launch → upgrade-over → uninstall with and without data deletion).
4. Document commands in `CLAUDE.md`.
- **Rollback:** delete the `installer\` directory and revert the csproj metadata; no runtime impact.

## Open Questions

- **Code signing certificate** — defer; will be a follow-up once a certificate is available. (Affects SmartScreen and future auto-update trust.)
- **Distribution channel** — GitHub Releases is the assumed target (and the natural source for a future auto-updater), but wiring it is out of scope here.
- **License/EULA page** — none exists yet; the wizard will omit the license page unless a `LICENSE` is added later.
