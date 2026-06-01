# CI/CD (Jenkins)

Mosaic is built, tested, and released by Jenkins from the [`Jenkinsfile`](../Jenkinsfile) at the
repo root. This document describes the pipeline and the one-time Jenkins setup it depends on.

## What the pipeline does

On **every push**: checks out, `dotnet build Mosaic.sln -c Release`, then `dotnet test`. A compile
error or any failing test fails the run and blocks everything after it.

On **`master`**, after a green build/test, the pipeline publishes a release **only when the version
was bumped**:

1. Reads `<Version>` from `Mosaic.csproj` — the single source of truth.
2. Checks whether a GitHub Release tagged `v<version>` already exists (`gh release view`).
3. If it does **not** exist, runs [`installer\package.ps1`](../installer/README.md) to produce
   `MosaicSetup-<version>.exe` + `.sha256`, then `gh release create v<version> …` uploading **both**
   assets, with the tag pointing at the exact built commit.
4. If it already exists, the run finishes green without packaging or publishing.

This is **idempotent** (re-running an already-published commit creates no duplicate release) and
**master-only** (other branches and PRs build and test but never publish). The published
`v<version>` release + `.sha256` are exactly what the in-app **auto-update** feature discovers and
verifies — see [`installer\README.md`](../installer/README.md).

### Releasing = bumping the version

There is **no automatic version bump**. To cut a release, bump `<Version>` in `Mosaic.csproj` and
push to `master`; that edit is the trigger. Pushing to `master` without changing `<Version>` runs CI
only. The pipeline reuses `installer\package.ps1` unchanged, so a local
`.\installer\package.ps1` and a CI release produce identical artifacts.

| Stage | When it runs | What it does |
|-------|--------------|--------------|
| Checkout | always | Captures the commit SHA and whether the branch is `master`. |
| Build | always | `dotnet build Mosaic.sln -c Release`. |
| Test | always | `dotnet test … --logger trx`; archives the `.trx`. |
| Detect release | `master` | Reads `<Version>`; sets `DO_RELEASE` if no `v<version>` release exists. |
| Package installer | `master` + new version | `installer\package.ps1 -Version <version>`. |
| Publish release | `master` + new version | `gh release create v<version>` with both assets. |

## One-time Jenkins setup

### 1. Windows build agent `munin`

WPF (`net10.0-windows` + `UseWPF`) and Inno Setup are **Windows-only**, so the build cannot run on
the Linux controller or in a Linux container (Windows containers need a Windows host). A dedicated
Windows agent is required; the `Jenkinsfile` pins `agent { label 'munin' }` (a Jenkins node's name
is also an implicit label). The Linux controller and other agents are unaffected.

Install on `munin` and put on `PATH`:

- **.NET 10 SDK** — `dotnet build` / `dotnet test` / `dotnet publish`.
- **Inno Setup 6** — provides `ISCC.exe`. `winget install --exact --id JRSoftware.InnoSetup`
  (or <https://jrsoftware.org/isdl.php>). `package.ps1` also probes the standard install locations.
- **GitHub CLI** (`gh`) — creates the release. `winget install --exact --id GitHub.cli`.

Record the installed versions (`dotnet --version`, `ISCC` banner, `gh --version`) so the environment
is reproducible.

### 2. GitHub token credential

Add a Jenkins **Secret text** credential with id **`mosaic-github-token`** holding a GitHub
**fine-grained PAT** scoped to `Frodenkvist/mosaic` with **Contents: read and write** (sufficient for
`gh release create` to push tags and releases). The `Jenkinsfile` binds it as `GH_TOKEN`, which
Jenkins masks in the console log; the token never appears in the repo.

### 3. Pipeline job

Create a **Pipeline** (or **Multibranch Pipeline**) job:

- **Definition:** *Pipeline script from SCM* → Git → `git@github.com:Frodenkvist/mosaic.git`,
  **Script Path** `Jenkinsfile`.
- Provide the SSH/credentials Jenkins needs to fetch the repo.
- A single-branch pipeline job should track `*/master`; a multibranch job discovers branches and
  the `Jenkinsfile`'s `master`-only gating still applies.

### 4. GitHub push webhook

- In the job, enable **"GitHub hook trigger for GITScm polling"** (the `triggers { githubPush() }`
  block in the `Jenkinsfile` declares this for single-branch jobs).
- In the GitHub repo: **Settings → Webhooks → Add webhook**, Payload URL
  `https://<your-jenkins>/github-webhook/`, content type `application/json`, event **Just the push
  event**.

## Validation

1. **CI only:** push a `master` commit that does *not* bump `<Version>` → expect Build + Test, no release.
2. **Non-master:** a branch/PR build runs Build + Test and never publishes.
3. **Release:** bump `<Version>`, push to `master` → expect a `v<version>` GitHub Release with both
   `MosaicSetup-<version>.exe` and its `.sha256`, the tag on the built commit.
4. **Idempotency:** re-run the released commit → no duplicate release; run still green.
5. **End to end:** an installed Mosaic client's auto-updater discovers and verifies the new release.

## Troubleshooting

- **Missing toolchain** (e.g. `ISCC.exe` not found): the Package stage fails fast with guidance and
  **no partial release is published**. Install the missing tool on `munin` and re-run.
- **Partial/bad release:** delete the GitHub Release and its `v<version>` tag; the idempotent gate
  republishes cleanly on the next `master` run.
- **`gh` auth errors** during *Detect release* are treated as "release missing" and the run will try
  to publish (then fail at create). Confirm `mosaic-github-token` is valid and scoped correctly.
- **Releases never trigger** in a single-branch job: confirm the branch resolves to `master`
  (the Checkout stage logs `master=true/false`).
