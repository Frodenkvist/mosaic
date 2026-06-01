## 1. Pipeline definition (Jenkinsfile)

- [x] 1.1 Create `Jenkinsfile` at the repo root: declarative pipeline, `agent { label 'munin' }` (the Windows agent), `options { disableConcurrentBuilds(); timestamps() }`.
- [x] 1.2 Add the `Checkout` stage (SCM) and capture the built commit SHA for use as the release target.
- [x] 1.3 Add the `Build` stage: `dotnet build Mosaic.sln -c Release` (fails the run on compile error).
- [x] 1.4 Add the `Test` stage: `dotnet test` for the solution; publish/record results so any failing test fails the run and skips later stages.
- [x] 1.5 Add a `Detect release` stage gated on `when { branch 'master' }`: read `<Version>` from `Mosaic.csproj`, run `gh release view v<version>`, and set a `RELEASE` flag = release does **not** yet exist.
- [x] 1.6 Add a `Package` stage gated on `master` + `RELEASE`: run `.\installer\package.ps1` (version defaults from `<Version>`); failure fails the run with no publish.
- [x] 1.7 Add a `Publish` stage gated on `master` + `RELEASE`: `gh release create v<version> installer\dist\MosaicSetup-<version>.exe installer\dist\MosaicSetup-<version>.exe.sha256 --target <commit> --title "Mosaic <version>" --generate-notes`.
- [x] 1.8 Bind the GitHub token from the Jenkins credential store as `GH_TOKEN` (e.g. `environment { GH_TOKEN = credentials('mosaic-github-token') }`); confirm it is masked in console output and absent from the repo.
- [x] 1.9 Add a `post` block that reports the final build/test/publish status.

## 2. Documentation

- [x] 2.1 Document the Jenkins setup in `installer\README.md` (or a new `docs/ci.md`): the Windows agent `munin`, toolchain (.NET 10 SDK, Inno Setup 6, GitHub CLI), `master` push trigger via GitHub webhook, and the `mosaic-github-token` credential.
- [x] 2.2 Note that releasing is triggered by bumping `<Version>` in `Mosaic.csproj` (no automatic version bump), and that the pipeline reuses `installer\package.ps1` and produces the `v<version>` release + `.sha256` consumed by auto-update.

## 3. Jenkins job & agent setup (infrastructure)

- [x] 3.1 Provision the Windows build agent `munin` (attached to the Linux controller) with .NET 10 SDK, Inno Setup 6 (`ISCC.exe`), and the GitHub CLI (`gh`) installed; verify it connects with the `munin` node label and record the tool versions.
- [x] 3.2 Add a Jenkins **Secret text** credential `mosaic-github-token` holding a fine-grained GitHub PAT scoped to `Frodenkvist/mosaic` with Contents read/write (sufficient for tags/releases).
- [x] 3.3 Create the Jenkins pipeline (or multibranch) job pointed at `git@github.com:Frodenkvist/mosaic.git`, using the in-repo `Jenkinsfile`.
- [x] 3.4 Configure the GitHub **push webhook** (repo → Settings → Webhooks → Jenkins `/github-webhook/`) and enable "GitHub hook trigger for GITScm polling" on the job so pushes to `master` trigger a build.

## 4. Validation

- [x] 4.1 CI-only run: push a `master` commit that does **not** bump `<Version>` → verify build + test run and **no** release is created.
- [x] 4.2 Non-master run: confirm a branch/PR build does not publish regardless of `<Version>`.
- [ ] 4.3 Release run: bump `<Version>`, push to `master` → verify a `v<version>` GitHub Release exists with both `MosaicSetup-<version>.exe` and its `.sha256` attached, and the tag points at the built commit.
- [x] 4.4 Idempotency: re-run the released commit → verify no duplicate/overwritten release and the run still succeeds.
- [x] 4.5 End-to-end: confirm an installed Mosaic client's auto-updater discovers and verifies the newly published release.
