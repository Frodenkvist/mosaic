## ADDED Requirements

### Requirement: Version-controlled pipeline definition
The repository SHALL contain the continuous-integration and release pipeline definition as a version-controlled file (a `Jenkinsfile` at the repository root), so that the build, test, and release stages are reviewed and changed alongside the code they build.

#### Scenario: Pipeline defined in the repository
- **WHEN** the Jenkins job for Mosaic runs
- **THEN** it executes the pipeline defined by the `Jenkinsfile` checked into the repository at the built commit
- **AND** changing the pipeline requires committing a change to that file

### Requirement: Continuous integration on master
The pipeline SHALL build and test the application on every commit pushed to the `master` branch, restoring dependencies, compiling the solution, and running the full test suite.

#### Scenario: Build and test on a master commit
- **WHEN** a commit is pushed to `master`
- **THEN** the pipeline restores dependencies, builds `Mosaic.sln`, and runs the test suite for that commit

#### Scenario: Pipeline reports the outcome
- **WHEN** the pipeline finishes for a `master` commit
- **THEN** its overall result reflects whether the build and tests succeeded

### Requirement: Failing build or tests block release
The pipeline SHALL fail when the build does not compile or any test fails, and SHALL NOT package or publish a release from a failed pipeline run.

#### Scenario: Test failure fails the pipeline
- **WHEN** the test suite reports one or more failing tests
- **THEN** the pipeline run is marked failed
- **AND** no installer is packaged and no GitHub Release is created

#### Scenario: Build failure fails the pipeline
- **WHEN** the solution fails to compile
- **THEN** the pipeline run is marked failed
- **AND** later stages (test, package, publish) do not run

### Requirement: Release publishing gated on a version bump
After a successful build and test on `master`, the pipeline SHALL determine the product version from `<Version>` in `Mosaic.csproj` (the single source of truth) and SHALL run the release steps only when no GitHub Release for that version (tag `v<version>`) already exists.

#### Scenario: New version triggers a release
- **WHEN** the build and tests pass on `master` and no GitHub Release tagged `v<version>` exists for the current `<Version>`
- **THEN** the pipeline proceeds to package the installer and publish the release

#### Scenario: Unchanged version does not release
- **WHEN** the build and tests pass on `master` and a GitHub Release tagged `v<version>` already exists for the current `<Version>`
- **THEN** the pipeline completes successfully without packaging or publishing a release

### Requirement: Automated installer packaging on release
When a release is triggered by a version bump, the pipeline SHALL produce the distributable installer and its checksum by running the repository's documented packaging command, yielding `MosaicSetup-<version>.exe` and `MosaicSetup-<version>.exe.sha256`.

#### Scenario: Packaging produces both assets
- **WHEN** a release is triggered for version `<version>`
- **THEN** the pipeline runs the packaging command and produces `MosaicSetup-<version>.exe`
- **AND** produces the matching `MosaicSetup-<version>.exe.sha256` checksum file

#### Scenario: Packaging failure fails the pipeline
- **WHEN** the packaging command fails (for example, the installer toolchain is missing or the publish fails)
- **THEN** the pipeline run is marked failed
- **AND** no GitHub Release is created

### Requirement: GitHub release publishing
When a release is triggered, the pipeline SHALL create a GitHub Release on `Frodenkvist/mosaic` tagged `v<version>` at the built commit and SHALL upload both the installer executable and its `.sha256` checksum as release assets, matching what the auto-update capability expects to discover and verify.

#### Scenario: Release created with both assets
- **WHEN** the pipeline publishes a release for version `<version>`
- **THEN** a GitHub Release tagged `v<version>` exists
- **AND** it has both `MosaicSetup-<version>.exe` and `MosaicSetup-<version>.exe.sha256` attached as assets

#### Scenario: Release tag points at the built commit
- **WHEN** the pipeline creates the release tag `v<version>`
- **THEN** the tag references the exact `master` commit that was built, tested, and packaged in that run

### Requirement: Idempotent, non-duplicating publishing
The pipeline SHALL NOT create a second GitHub Release for a version that already has one, so that re-running a build for an already-published commit is safe and does not produce duplicate or conflicting releases.

#### Scenario: Re-running an already-published version
- **WHEN** the pipeline runs for a `master` commit whose `<Version>` already has a published GitHub Release
- **THEN** the run does not create or overwrite a release for that version
- **AND** the run still completes successfully (build and tests reported)

### Requirement: Releases only from master
The pipeline SHALL publish releases only for builds of the `master` branch, and SHALL NOT create a GitHub Release for builds of any other branch or for pull-request builds.

#### Scenario: Non-master build does not publish
- **WHEN** the pipeline runs for a branch other than `master` (or for a pull request)
- **THEN** it may build and test
- **AND** it does not package or publish a release regardless of the `<Version>` value

### Requirement: Secure release credential handling
The pipeline SHALL authenticate to GitHub for release creation using a credential provided by the Jenkins credential store, and SHALL NOT contain the token in the repository or expose it in build logs.

#### Scenario: Token sourced from Jenkins credentials
- **WHEN** the pipeline needs to create a GitHub Release
- **THEN** it obtains the GitHub token from a Jenkins-managed credential
- **AND** the token value does not appear in the repository or in the pipeline's console output

### Requirement: Documented build agent requirements
The repository SHALL document the build environment the pipeline requires — a Windows agent with the .NET 10 SDK, Inno Setup 6, and the GitHub CLI installed, plus the GitHub token credential — so that the Jenkins job can be reproduced, and a release run that lacks a required tool SHALL fail with a clear error rather than producing an incomplete release.

#### Scenario: Documented prerequisites
- **WHEN** a maintainer sets up or reproduces the Jenkins job
- **THEN** the repository documents the required Windows agent, toolchain (.NET 10 SDK, Inno Setup 6, GitHub CLI), branch trigger, and credential

#### Scenario: Missing toolchain fails clearly
- **WHEN** a release run executes on an agent missing a required tool (for example, the Inno Setup compiler)
- **THEN** the run fails with an error identifying the missing dependency
- **AND** no partial or unverifiable GitHub Release is published
