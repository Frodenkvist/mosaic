# installer Specification

## Purpose
Defines how Mosaic is packaged, distributed, installed, upgraded, and uninstalled on Windows: a self-contained (no-prerequisite) win-x64 build wrapped in a wizard-driven, per-user installer (no admin) with shortcuts and Apps & features registration, a standalone uninstaller that preserves the user's library by default, in-place upgrades, and a single reproducible packaging command.
## Requirements
### Requirement: Self-contained packaging
The build SHALL produce a self-contained, win-x64 publish of Mosaic that bundles the .NET 10 Desktop runtime, so that the installed application runs on a clean Windows 10/11 x64 machine with no separately installed runtime or other prerequisites.

#### Scenario: Publish bundles the runtime
- **WHEN** the packaging build runs the publish step
- **THEN** the published output is self-contained for `win-x64`
- **AND** it includes the .NET runtime and all application assemblies required to run `Mosaic.exe` without a machine-wide .NET installation

#### Scenario: Runs on a machine without .NET installed
- **WHEN** the produced installer is run on a Windows x64 machine that does not have the .NET 10 Desktop runtime installed
- **THEN** Mosaic installs and launches successfully

### Requirement: Wizard installer executable
The build SHALL produce a single distributable installer executable named `MosaicSetup-<version>.exe` that guides the user through installation with a wizard.

#### Scenario: Wizard pages
- **WHEN** the user runs the installer executable
- **THEN** the wizard presents a welcome page, an install-location page, an additional-tasks page, an installation-progress page, and a finish page
- **AND** the finish page offers an option to launch Mosaic immediately

#### Scenario: Installer filename carries the version
- **WHEN** the packaging build completes for product version `<version>`
- **THEN** the output file is named `MosaicSetup-<version>.exe`
- **AND** the installer reports `<version>` as the product version

### Requirement: Per-user installation without elevation
The installer SHALL install Mosaic per-user without requiring administrator privileges, into a user-writable program directory, separate from Mosaic's data directory.

#### Scenario: No elevation prompt
- **WHEN** a standard (non-administrator) user runs the installer
- **THEN** installation completes without a UAC elevation prompt

#### Scenario: Default install location
- **WHEN** the user accepts the default install location
- **THEN** Mosaic is installed under `%LocalAppData%\Programs\Mosaic`
- **AND** the install directory is distinct from the data directory `%LocalAppData%\Mosaic`

#### Scenario: Custom install location
- **WHEN** the user changes the install location on the install-location page
- **THEN** Mosaic is installed into the chosen directory

### Requirement: Shortcuts
The installer SHALL create a Start Menu shortcut, and SHALL create a desktop shortcut only when the user opts in.

#### Scenario: Start Menu shortcut always created
- **WHEN** installation completes
- **THEN** a Start Menu shortcut that launches `Mosaic.exe` exists

#### Scenario: Optional desktop shortcut
- **WHEN** the user selects the "create a desktop shortcut" task and installation completes
- **THEN** a desktop shortcut that launches `Mosaic.exe` exists

#### Scenario: Desktop shortcut declined by default
- **WHEN** the user does not select the desktop-shortcut task
- **THEN** no desktop shortcut is created

### Requirement: Apps & features registration
The installer SHALL register Mosaic in Windows Apps & features (Add/Remove Programs) with its display name, version, publisher, and icon, and SHALL provide a working uninstall entry there.

#### Scenario: Listed in Apps & features
- **WHEN** installation completes
- **THEN** Mosaic appears in Apps & features with its display name, version, and publisher
- **AND** an uninstall action is available from that entry

### Requirement: Standalone uninstaller
The installation SHALL include a standalone uninstaller executable that removes the application's program files, its shortcuts, and its Apps & features registration.

#### Scenario: Uninstall removes program files and shortcuts
- **WHEN** the user runs the uninstaller
- **THEN** the installed program files are removed
- **AND** the Start Menu shortcut (and the desktop shortcut, if it was created) are removed
- **AND** the Apps & features entry is removed

### Requirement: User data preserved by default on uninstall
The uninstaller SHALL preserve the user's data (game library database, artwork cache, and settings under `%LocalAppData%\Mosaic`) by default, and SHALL delete it only when the user explicitly opts in.

#### Scenario: Default uninstall keeps data
- **WHEN** the user uninstalls Mosaic without choosing to delete data
- **THEN** the directory `%LocalAppData%\Mosaic` and its contents remain on disk

#### Scenario: Opt-in data deletion
- **WHEN** the user selects the option to also delete their data during uninstall
- **THEN** the directory `%LocalAppData%\Mosaic` and its contents are removed

#### Scenario: Reinstall preserves library
- **WHEN** the user uninstalls Mosaic with the default (keep data) option and later reinstalls it
- **THEN** the previously created game library, artwork, and settings are available in the reinstalled app

### Requirement: In-place upgrade
The installer SHALL upgrade an existing installation in place, replacing program files without deleting the user's data.

#### Scenario: Installing a newer version over an existing one
- **WHEN** the installer for a newer version runs on a machine where an earlier version is installed
- **THEN** the existing installation is replaced with the newer version in the same location
- **AND** the user's data under `%LocalAppData%\Mosaic` is retained
- **AND** the Apps & features entry reflects the newer version

#### Scenario: Application in use during upgrade
- **WHEN** Mosaic is running while an upgrade install is started
- **THEN** the installer prompts the user to close the running application before continuing

### Requirement: Reproducible packaging command
The repository SHALL provide a single documented command that produces the installer executable, and that command SHALL fail with clear guidance when the required installer toolchain is not present.

#### Scenario: One command builds the installer
- **WHEN** a developer runs the documented packaging command on a machine with the installer toolchain present
- **THEN** the self-contained publish is produced and compiled into `MosaicSetup-<version>.exe`

#### Scenario: Missing toolchain reported clearly
- **WHEN** the packaging command is run on a machine where the Inno Setup compiler (`ISCC.exe`) is not installed
- **THEN** the command fails with a message identifying the missing dependency and how to install it

