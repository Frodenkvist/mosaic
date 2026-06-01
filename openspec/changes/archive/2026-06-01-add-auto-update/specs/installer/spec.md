## ADDED Requirements

### Requirement: Unattended silent in-place upgrade
The installer SHALL support running in a silent (unattended) mode that performs an in-place upgrade with no user interaction and no elevation prompt, so that the in-app updater can apply an update programmatically.

#### Scenario: Silent upgrade requires no interaction
- **WHEN** the installer for a newer version is run in silent mode over an existing installation
- **THEN** the upgrade completes without showing wizard pages or requiring user input
- **AND** no administrator elevation prompt is shown

#### Scenario: Silent upgrade preserves user data
- **WHEN** a silent in-place upgrade completes
- **THEN** the existing installation is replaced with the newer version in the same location
- **AND** the user's data under `%LocalAppData%\Mosaic` is retained

### Requirement: Relaunch application after a silent update
The installer SHALL relaunch Mosaic after completing a silent upgrade when the updater requests it via a dedicated command-line flag, and SHALL NOT auto-launch on a normal interactive silent install that does not request a relaunch.

#### Scenario: Relaunch when requested
- **WHEN** the installer is run silently with the relaunch flag set and the upgrade completes
- **THEN** the installer starts the updated `Mosaic.exe`

#### Scenario: No unexpected launch without the flag
- **WHEN** the installer is run silently without the relaunch flag
- **THEN** the installer does not launch Mosaic

### Requirement: Published installer checksum
The packaging process SHALL produce a SHA-256 checksum file for the installer executable so that a downloaded installer can be verified for integrity, and the checksum SHALL be published alongside the installer asset.

#### Scenario: Checksum produced during packaging
- **WHEN** the packaging command builds `MosaicSetup-<version>.exe`
- **THEN** a corresponding SHA-256 checksum file for that installer is also produced

#### Scenario: Checksum matches the installer
- **WHEN** the SHA-256 of the produced installer is computed
- **THEN** it equals the value recorded in the published checksum file
