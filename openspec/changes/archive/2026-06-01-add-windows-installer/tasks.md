## 1. Project versioning & publish metadata

- [x] 1.1 Add `<Version>`, `<AssemblyVersion>`, `<FileVersion>`, `<Product>`, `<Company>`, and `<Authors>` to `Mosaic.csproj` (icon already set via `<ApplicationIcon>`)
- [x] 1.2 Add publish-friendly properties for self-contained win-x64 (`<RuntimeIdentifier>win-x64</RuntimeIdentifier>` and `<SelfContained>true</SelfContained>` under a publish-only condition, or rely on CLI flags) without changing the normal `dotnet build`/`dotnet run` dev flow
- [x] 1.3 Verify `dotnet publish -c Release -r win-x64 --self-contained true` emits a runnable `Mosaic.exe` with the bundled runtime into a clean publish folder

## 2. Installer toolchain setup

- [x] 2.1 Install Inno Setup locally (`winget install JRSoftware.InnoSetup`) and confirm `ISCC.exe` is available
- [x] 2.2 Create the `installer\` directory; add a short `installer\README.md` describing the toolchain dependency and how to build

## 3. Inno Setup script (`installer\Mosaic.iss`)

- [x] 3.1 Define `[Setup]` metadata: stable `AppId` GUID, `AppName=Mosaic`, `AppPublisher`, `AppVersion`/`VersionInfoVersion` from a `MyAppVersion` define, `SetupIconFile=..\Mosaic.ico`, LZMA2 compression
- [x] 3.2 Configure per-user install: `PrivilegesRequired=lowest`, `DefaultDirName={localappdata}\Programs\Mosaic`, `DefaultGroupName=Mosaic`, `OutputBaseFilename=MosaicSetup-{#MyAppVersion}`, `OutputDir=dist`
- [x] 3.3 `[Files]`: include the entire self-contained publish folder recursively (`Source: ..\publish\*; Flags: recursesubdirs ignoreversion`)
- [x] 3.4 `[Icons]`: always create the Start Menu shortcut to `Mosaic.exe`; create a desktop shortcut gated on a `[Tasks]` entry
- [x] 3.5 `[Tasks]`: add an unchecked-by-default `desktopicon` task
- [x] 3.6 `[Run]`: add a "Launch Mosaic" finish-page checkbox (`postinstall nowait skipifsilent`)
- [x] 3.7 Ensure the app is closed before upgrade/uninstall (`CloseApplications`/`RestartApplications` or equivalent) so in-use files don't block

## 4. Uninstall data-handling logic

- [x] 4.1 Add an Inno Setup `[Code]` routine that, during uninstall, shows an unchecked-by-default option "Also delete my game library, artwork, and settings"
- [x] 4.2 When opted in, recursively delete `%LocalAppData%\Mosaic` (`{localappdata}\Mosaic`); when not opted in, leave it untouched
- [x] 4.3 Confirm the standard uninstall removes only program files, shortcuts, and the Apps & features entry

## 5. Packaging script (`installer\package.ps1`)

- [x] 5.1 Accept/derive a version parameter (default: read from `Mosaic.csproj`)
- [x] 5.2 Clean prior output, then run the self-contained `dotnet publish` into `installer\publish\`
- [x] 5.3 Locate `ISCC.exe` (default install path or PATH); if missing, exit non-zero with a clear "install Inno Setup" message
- [x] 5.4 Invoke `ISCC.exe installer\Mosaic.iss /DMyAppVersion=<version>` and emit `MosaicSetup-<version>.exe` into `installer\dist\`
- [x] 5.5 Add `installer\publish\` and `installer\dist\` to `.gitignore`

## 6. Verification (manual smoke test)

- [x] 6.1 Run `installer\package.ps1` and confirm `MosaicSetup-<version>.exe` is produced
- [ ] 6.2 Install on a clean/x64 environment with no .NET installed; confirm no UAC prompt, Start Menu shortcut works, and the app launches and creates its data dir
- [x] 6.3 Confirm Mosaic appears in Apps & features with correct name/version/publisher
- [ ] 6.4 Add a game, then run an in-place upgrade install (bump version) and confirm the library and artwork are retained and the version updates
- [x] 6.5 Uninstall with default option; confirm program files/shortcuts removed and `%LocalAppData%\Mosaic` retained
- [ ] 6.6 Reinstall and confirm the prior library is present; then uninstall with the "delete data" option and confirm `%LocalAppData%\Mosaic` is removed

## 7. Documentation

- [x] 7.1 Add a "Packaging / Release" section to `CLAUDE.md` documenting `installer\package.ps1`, the Inno Setup dependency, and the per-user/self-contained choices
- [x] 7.2 Note the unsigned-installer SmartScreen caveat and that code signing is a future follow-up
