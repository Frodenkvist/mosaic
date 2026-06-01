## Context

Mosaic is a Windows-only WPF/.NET 10 desktop app. Releasing is currently fully manual: bump `<Version>` in `Mosaic.csproj`, run `installer\package.ps1` (self-contained win-x64 publish → Inno Setup → `MosaicSetup-<version>.exe` + `.sha256`), then `gh release create v<version>` uploading both assets. The in-app **auto-update** capability already consumes exactly these artifacts — it reads the latest GitHub Release of `Frodenkvist/mosaic`, compares the `v<version>` tag, downloads the installer, and verifies it against the published `.sha256`. So the producer side just needs automating.

The developer runs a Jenkins instance. This change wires Mosaic into it: verify every `master` commit, and publish a release automatically when (and only when) the version is bumped. The remote is `git@github.com:Frodenkvist/mosaic.git`.

**Hard constraint:** the build targets `net10.0-windows` with `UseWPF`, and packaging shells out to Inno Setup (`ISCC.exe`). Both require **Windows** — there is no Linux build path. Whatever host Jenkins runs on, the pipeline must execute on a Windows agent.

## Goals / Non-Goals

**Goals:**
- A version-controlled `Jenkinsfile` that builds and tests every `master` commit.
- Automatic GitHub Release on a version bump, reusing `installer\package.ps1` unchanged, producing exactly the assets `auto-update` expects (`v<version>` tag, installer, `.sha256`).
- Idempotent and safe: re-running a commit never double-publishes; non-`master` builds never publish; failing tests block any release.
- Reproducible job setup documented in the repo (agent, tooling, trigger, credential).

**Non-Goals:**
- **Automatic version bumping.** The human edits `<Version>` in `Mosaic.csproj`; that edit *is* the release trigger. The pipeline never computes or commits a version.
- **Code signing.** The installer stays unsigned (a separately tracked follow-up); integrity is covered by the `.sha256`, not authenticity.
- **Multi-platform / multi-RID builds.** win-x64 self-contained only, matching today's installer.
- **Provisioning Jenkins itself** (installing the controller, agents, plugins) — documented as prerequisites, not coded.

## Decisions

### Declarative `Jenkinsfile` at the repo root
Pipeline-as-code, reviewed with the code it builds, versioned per commit (satisfies "version-controlled pipeline definition"). Declarative (over scripted) for readability and the built-in `when`/`post` constructs used for gating and reporting.
- *Alternative — Jenkins freestyle/job-DSL config:* rejected; lives in Jenkins, not the repo, and isn't reviewable per commit.

### Windows agent `munin` selected by node label
The Jenkins controller and its existing agents run on **Linux**, which cannot build this app: a Linux Docker daemon can only run Linux containers, and WPF's markup compilation is Windows-only — so even CI build/test (the app and the test project target `net10.0-windows`) requires Windows, not just the installer step. A dedicated **Windows agent named `munin`** is attached to the Linux controller; the pipeline pins `agent { label 'munin' }` (a Jenkins node's name is also an implicit label). `munin` needs: **.NET 10 SDK**, **Inno Setup 6** (`ISCC.exe` on `PATH` or in a location `package.ps1` already probes), and the **GitHub CLI** (`gh`). Stages run under PowerShell (`powershell`/`pwsh`) since `package.ps1` is PowerShell and the host is Windows. The Linux controller and other agents are unaffected.
- *Alternative — Docker on the Linux agents:* ruled out; Windows containers require a Windows host, and WPF will not build in a Linux container.
- *Alternative — Wine / cross-compile on Linux:* `ISCC.exe` runs under Wine, but the self-contained WPF publish it packages cannot be produced without Windows.

### Version-bump detection by release existence, not diffing
The "did the version change?" gate checks **whether a GitHub Release tagged `v<version>` already exists** (`gh release view v<version>`), rather than diffing `Mosaic.csproj` between commits. Rationale:
- Idempotent by construction — re-builds, retries, and force-pushes of an already-published commit naturally no-op.
- Robust to history rewrites and to the first run (no "previous commit" to diff against).
- Single source of truth stays `<Version>`; the check is "is this version published yet?", which is exactly the question.
- *Alternative — `git diff` the `<Version>` line vs the parent commit:* fragile (squash/rebase/re-run can mis-fire or miss), and a re-run of the same commit would try to republish. Rejected.

### Reuse `installer\package.ps1` verbatim
The release stage calls `.\installer\package.ps1` (version defaults from `<Version>`), which already produces both `MosaicSetup-<version>.exe` and the `.sha256`, and already fails fast with clear guidance when `ISCC.exe` is absent. No publish logic is duplicated in the `Jenkinsfile`.

### Publish with the GitHub CLI in a single create-with-assets call
`gh release create v<version> <installer> <sha256> --target <commit> --title "Mosaic <version>" --generate-notes`. Doing the tag + release + both asset uploads in one command keeps publishing close to atomic (no window where a tag exists with no/partial assets) and auto-generates notes from commits since the last release. `gh` authenticates from `GH_TOKEN`.
- *Alternative — raw GitHub REST via `Invoke-RestMethod`:* more moving parts (separate create, then per-asset upload calls) for no benefit when `gh` is available. Documented as a fallback only.

### Credentials via Jenkins credential store, injected as `GH_TOKEN`
A Jenkins **Secret text** credential (id e.g. `mosaic-github-token`) holding a GitHub token with permission to create releases on `Frodenkvist/mosaic` is bound with `withCredentials`/`environment { GH_TOKEN = credentials('mosaic-github-token') }`. Jenkins masks the value in logs; it never lives in the repo. A **fine-grained PAT** scoped to just this repo's *Contents* (read/write, for tags/releases) is preferred over a classic broad-scope token.

### Branch gating with `when { branch 'master' }`
Release stages are wrapped so they run only on `master`; other branches and PRs build/test but never publish. `options { disableConcurrentBuilds() }` avoids two runs racing on the same release tag. Builds are triggered by a **GitHub push webhook** to Jenkins (repo webhook → Jenkins `/github-webhook/`; the job enables "GitHub hook trigger for GITScm polling"); the in-pipeline branch gating then decides what publishes.

### Pipeline shape
```
options: disableConcurrentBuilds, timestamps
stage Checkout        → scm
stage Build           → dotnet build Mosaic.sln -c Release
stage Test            → dotnet test (publishes results; failure fails the run)
stage Detect release  → only on master: read <Version>; gh release view v<version>;
                        set RELEASE = (not found)
stage Package         → when RELEASE: .\installer\package.ps1
stage Publish         → when RELEASE: gh release create v<version> <assets> --generate-notes
post                  → report status
```

## Risks / Trade-offs

- **Jenkins host may be Linux** → no Windows build path exists. *Mitigation:* attach a Windows agent (VM or physical) labeled `windows`; the `Jenkinsfile` already targets it by label. This is the prerequisite the setup docs lead with. Resolve before applying (see Open Questions).
- **Webhook not reachable** (Jenkins behind NAT/firewall) → commits don't trigger builds. *Mitigation:* fall back to `pollSCM`; the publish gating is unaffected.
- **GitHub token leakage / over-broad scope** → release infra compromise. *Mitigation:* fine-grained PAT limited to the one repo's Contents; bound via the credential store and masked in logs; rotate on suspicion.
- **Partial release on mid-publish failure** → a tag exists with missing assets, which `auto-update` would then ignore (it requires the installer + `.sha256`). *Mitigation:* package fully first, then create the release with both assets in one `gh` call; the idempotent gate lets a re-run complete it (or delete the bad tag, see rollback).
- **Concurrent builds racing the same tag** → duplicate/failed publish. *Mitigation:* `disableConcurrentBuilds()` plus the release-existence check.
- **`gh`/SDK version drift on the agent** → command flags change. *Mitigation:* pin/record the agent toolchain versions in the setup docs; the missing-tool path fails clearly rather than half-publishing.

## Migration Plan

1. Add `Jenkinsfile` to the repo (this change) and document the job setup (agent, tooling, trigger, credential) in the installer/release docs.
2. Provision the **Windows agent `munin`** (attached to the Linux controller) with .NET 10 SDK, Inno Setup 6, and `gh`.
3. Add the **`mosaic-github-token`** secret-text credential in Jenkins (fine-grained PAT, Contents read/write on `Frodenkvist/mosaic`).
4. Create the Jenkins **pipeline job** (or multibranch) pointed at `git@github.com:Frodenkvist/mosaic.git`, and configure the GitHub **push webhook** to trigger it on `master`.
5. **Validate CI:** push a `master` commit that does *not* bump the version → expect build + test, no release.
6. **Validate release:** bump `<Version>` and push → expect a `v<version>` GitHub Release with both assets; confirm an installed client's auto-updater discovers it.

**Rollback:** disable or delete the Jenkins job to stop automation (no app code is affected — reverting the `Jenkinsfile` is the only repo change). If a bad/partial release is published, delete that GitHub Release and its `v<version>` tag; the idempotent gate will republish cleanly on the next run once fixed.

## Resolved

- **Build host:** the Linux controller and existing agents stay as-is; a dedicated Windows agent **`munin`** is added for Mosaic builds. Docker on the Linux agents was ruled out (Windows containers need a Windows host; WPF won't build in a Linux container).
- **Trigger:** a GitHub **push webhook** to Jenkins triggers builds on `master`.

## Open Questions

- **Release notes:** auto-generate from commits (`--generate-notes`) for now, or curate? Default is auto-generate; can switch to curated later without spec changes.
- **Credential label/scope:** confirm the credential id (`mosaic-github-token`) and that a fine-grained PAT with Contents read/write suffices for `gh release create` on this repo.
