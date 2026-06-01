## Why

Mosaic currently has no automated build or release process: every commit is built and tested only on the developer's machine, and cutting a release is a manual sequence (bump `<Version>`, run `installer\package.ps1`, then `gh release create` with two assets). This is error-prone — a release can ship without passing tests, or with a missing/ mismatched `.sha256` (which the in-app auto-updater refuses to apply). The developer already runs a Jenkins instance, so wiring Mosaic into it gives continuous verification of `master` and a hands-off, repeatable release whenever the version is bumped.

## What Changes

- Add a version-controlled **`Jenkinsfile`** (declarative pipeline) at the repo root defining the build/test/release stages.
- **CI on every push to `master`**: restore, `dotnet build Mosaic.sln`, and `dotnet test`. A failing build or test fails the pipeline and blocks any release.
- **Version-bump detection**: after tests pass on `master`, the pipeline reads `<Version>` from `Mosaic.csproj` (the single source of truth) and checks whether a GitHub Release tagged `v<version>` already exists. Release steps run only when it does not (idempotent — re-running a commit never double-publishes).
- **Automated release on bump**: when the version is new, the pipeline runs `installer\package.ps1` to produce `MosaicSetup-<version>.exe` + its `.sha256`, then creates a GitHub Release tagged `v<version>` and uploads **both** assets — exactly what the existing `auto-update` capability expects to consume.
- Document the Jenkins job setup: a **Windows build agent** (the WPF self-contained publish + Inno Setup require Windows), required tooling (.NET 10 SDK, Inno Setup 6, GitHub CLI), the `master` push trigger, and a GitHub token stored as a Jenkins credential (never in the repo).

## Capabilities

### New Capabilities
- `release-pipeline`: Continuous integration and automated release publishing driven by Jenkins — building and testing every `master` commit, detecting a version bump, packaging the installer, and publishing a verified GitHub Release so the in-app auto-updater can distribute it.

### Modified Capabilities
<!-- None. The auto-update and installer capabilities are unchanged; this change feeds the artifacts they already consume/produce. -->

## Impact

- **New file**: `Jenkinsfile` at the repo root (declarative pipeline). No application code changes.
- **Reuses existing tooling**: `installer\package.ps1` and `Mosaic.iss` are invoked as-is; `<Version>` in `Mosaic.csproj` remains the single source of truth.
- **External / infrastructure** (outside the repo, documented not coded here):
  - A Jenkins **Windows agent** with .NET 10 SDK, Inno Setup 6 (`ISCC.exe`), and the GitHub CLI (`gh`) installed.
  - A Jenkins **pipeline job** pointed at `git@github.com:Frodenkvist/mosaic.git`, triggered on push to `master` (webhook or SCM poll).
  - A **GitHub token** credential in Jenkins (repo/release scope) used to authenticate `gh` for release creation.
- **Consumed by**: the `auto-update` capability — releases this pipeline publishes (tag `v<version>` + installer + `.sha256`) are what installed clients discover and apply.
