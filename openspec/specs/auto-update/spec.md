# auto-update Specification

## Purpose
TBD - created by archiving change add-auto-update. Update Purpose after archive.
## Requirements
### Requirement: Update checks limited to installed builds
The application SHALL perform update checks and offer update actions only when it is running as an installed build, and SHALL treat update functionality as unavailable (a no-op) for non-installed copies such as a development run or an unpacked/portable copy.

#### Scenario: Installed build is update-capable
- **WHEN** the application is running from an installation produced by the Mosaic installer (the installer's standalone uninstaller is present alongside the executable)
- **THEN** update checks and the "Update now" action are available

#### Scenario: Development run does not check for updates
- **WHEN** the application is run from source (e.g. `dotnet run`) or any copy without the installer's uninstaller alongside the executable
- **THEN** no update check network request is made
- **AND** an on-demand check reports that updates are managed by the installer and not available for this build

### Requirement: Update source and version comparison
The application SHALL determine the latest available version from the project's GitHub Releases over HTTPS, and SHALL consider an update available only when the latest published version is strictly greater than the running build's version.

#### Scenario: Newer version published
- **WHEN** an update check runs and the latest published release version is greater than the running build's version
- **THEN** an update is reported as available with the newer version

#### Scenario: Already on the latest version
- **WHEN** an update check runs and the latest published release version is equal to or lower than the running build's version
- **THEN** no update is reported as available

#### Scenario: Unparseable or missing release information
- **WHEN** an update check runs and the latest release has no usable version tag or no installer asset
- **THEN** no update is reported as available
- **AND** the application continues to run normally

### Requirement: Automatic background update check
When automatic update checks are enabled, the application SHALL check for updates in the background shortly after startup without blocking the user interface, and SHALL throttle automatic checks so they run at most once per day.

#### Scenario: Background check after startup
- **WHEN** the application starts as an installed build with automatic update checks enabled and the last automatic check was more than a day ago
- **THEN** an update check runs in the background after the main window is shown
- **AND** the user interface remains responsive throughout the check

#### Scenario: Throttled automatic check
- **WHEN** the application starts and an automatic update check already ran within the last day
- **THEN** no additional automatic check is performed on this startup

#### Scenario: Automatic checks disabled
- **WHEN** the application starts with automatic update checks disabled
- **THEN** no automatic update check is performed

### Requirement: On-demand update check
The application SHALL provide a user-initiated "Check for updates" action that runs a check immediately regardless of the automatic-check throttle, and SHALL report the outcome (up to date, update available, or check failed) to the user.

#### Scenario: Manual check finds an update
- **WHEN** the user triggers "Check for updates" and a newer version is available
- **THEN** the user is informed that an update is available and offered to apply it

#### Scenario: Manual check when up to date
- **WHEN** the user triggers "Check for updates" and no newer version is available
- **THEN** the user is informed that the application is up to date

#### Scenario: Manual check fails
- **WHEN** the user triggers "Check for updates" and the check cannot complete (e.g. no network or the release source is unreachable)
- **THEN** the user is informed that the update check could not be completed
- **AND** the application continues to run normally

### Requirement: Update notification
When an update is available, the application SHALL notify the user non-intrusively and offer to either apply the update now or dismiss it for later, and SHALL NOT apply an update without the user's consent.

#### Scenario: Notified with a choice
- **WHEN** an update is found to be available
- **THEN** the user is shown the available version and offered "Update now" and "Later" choices

#### Scenario: Dismissing the update
- **WHEN** the user chooses "Later"
- **THEN** no update is downloaded or applied
- **AND** the application continues to run normally

### Requirement: Update download with integrity verification
When the user chooses to apply an update, the application SHALL download the installer for the new version over HTTPS and SHALL verify it against the published SHA-256 checksum before executing it, aborting the update and discarding the downloaded file if verification fails.

#### Scenario: Verified download proceeds
- **WHEN** the user chooses "Update now" and the downloaded installer's SHA-256 matches the published checksum
- **THEN** the update proceeds to the apply step

#### Scenario: Integrity check fails
- **WHEN** the downloaded installer's SHA-256 does not match the published checksum
- **THEN** the installer is not executed
- **AND** the downloaded file is deleted
- **AND** the user is informed that the update could not be verified

### Requirement: Apply update in place and relaunch
When the user applies a verified update, the application SHALL run the downloaded installer to upgrade in place without requiring administrator elevation, close the running application so its files are not locked, and relaunch the updated application when the upgrade completes, while preserving the user's data.

#### Scenario: Update applied and app relaunched
- **WHEN** a verified update is applied
- **THEN** the installer upgrades the installation in place without an elevation prompt
- **AND** the running application closes so the upgrade can replace its files
- **AND** the updated application is relaunched after the upgrade completes

#### Scenario: User data preserved across update
- **WHEN** an update has been applied
- **THEN** the user's game library, artwork, and settings under the data directory are retained and available in the updated application

### Requirement: Automatic-update preference
The application SHALL persist a user preference controlling whether automatic background update checks run, defaulting to enabled, and SHALL let the user change it.

#### Scenario: Preference persists
- **WHEN** the user changes the automatic-update preference and it is saved
- **THEN** the new preference is retained across application restarts

#### Scenario: Default when unset
- **WHEN** the application loads settings that do not contain an automatic-update preference
- **THEN** automatic update checks default to enabled

### Requirement: Current version visibility
The application SHALL display the currently installed version to the user in its settings.

#### Scenario: Version shown in settings
- **WHEN** the user opens settings
- **THEN** the currently running build's version is shown

### Requirement: Best-effort, non-blocking update behavior
All update checks, downloads, and version comparisons SHALL be best-effort and SHALL NOT block, freeze, or crash the application when the network is unavailable, the release source is unreachable, rate-limited, or returns unexpected data.

#### Scenario: Failure does not disrupt the app
- **WHEN** any update operation fails for any reason
- **THEN** the failure is handled gracefully
- **AND** the application remains usable with no loss of normal functionality

