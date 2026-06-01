// Jenkinsfile — Mosaic CI/CD pipeline.
//
// CI: builds and tests every push. On `master`, if <Version> in Mosaic.csproj names a
// version that has no GitHub Release yet, it packages the installer and publishes a
// GitHub Release (tag v<version> + MosaicSetup-<version>.exe + .sha256) — the exact
// artifacts the in-app auto-updater consumes.
//
// Runs on the Windows agent `munin`: WPF and Inno Setup are Windows-only, so the Linux
// controller/agents cannot build this app. See docs/ci.md for the full Jenkins setup
// (agent tooling, the `mosaic-github-token` credential, and the GitHub push webhook).

pipeline {
    agent { label 'munin' }

    options {
        disableConcurrentBuilds()   // avoid two runs racing on the same release tag
        skipDefaultCheckout()       // we check out explicitly to capture the commit SHA
    }

    triggers {
        // Build on GitHub push (the repo webhook hits Jenkins /github-webhook/).
        // Ignored by multibranch jobs, which trigger via branch indexing instead.
        githubPush()
    }

    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO = 'true'
        // gh reads these: GH_REPO sets the default repo; GH_TOKEN authenticates release calls.
        // GH_TOKEN is sourced from the Jenkins credential store and masked in the console log;
        // it never lives in the repo.
        GH_REPO  = 'Frodenkvist/mosaic'
        GH_TOKEN = credentials('mosaic-github-token')
    }

    stages {
        stage('Checkout') {
            steps {
                script {
                    def scmVars = checkout scm
                    // Exact commit the release tag will point at.
                    env.GIT_COMMIT = scmVars.GIT_COMMIT
                    // BRANCH_NAME is set for multibranch jobs; GIT_BRANCH (e.g. origin/master)
                    // for a single-branch pipeline job. Releasing is gated to master only.
                    def branch = env.BRANCH_NAME ?: scmVars.GIT_BRANCH ?: ''
                    env.IS_MASTER = (branch == 'master' || branch.endsWith('/master')).toString()
                    echo "Commit ${env.GIT_COMMIT} on '${branch}' (master=${env.IS_MASTER})."
                }
            }
        }

        stage('Build') {
            steps {
                // `bat` propagates the process exit code, so a compile error fails the run
                // and declarative pipeline skips the later stages.
                bat 'dotnet build Mosaic.sln -c Release'
            }
        }

        stage('Test') {
            steps {
                bat 'dotnet test Mosaic.sln -c Release --no-build --logger "trx;LogFileName=test-results.trx" --results-directory TestResults'
            }
            post {
                always {
                    archiveArtifacts artifacts: 'TestResults/**/*.trx', allowEmptyArchive: true
                    // To surface results in the Jenkins UI, install the MSTest plugin and enable:
                    // mstest testResultsFile: 'TestResults/**/*.trx', failOnError: false
                }
            }
        }

        stage('Detect release') {
            when { expression { env.IS_MASTER == 'true' } }
            steps {
                script {
                    // <Version> in Mosaic.csproj is the single source of truth (the same value
                    // installer\package.ps1 reads). Parsed in-process with the core readFile step
                    // (string indexOf/substring — no regex Matcher, which is not CPS-serializable)
                    // so the pipeline needs no PowerShell-step plugin.
                    def csproj = readFile('Mosaic.csproj')
                    def open  = csproj.indexOf('<Version>')
                    def close = csproj.indexOf('</Version>')
                    if (open < 0 || close < 0) { error 'Could not read <Version> from Mosaic.csproj' }
                    env.MOSAIC_VERSION = csproj.substring(open + '<Version>'.length(), close).trim()

                    // Idempotent gate: release only when no GitHub Release for this version exists.
                    // `gh release view` exits non-zero when the release is absent.
                    def code = bat(returnStatus: true, script: "gh release view v${env.MOSAIC_VERSION} >nul 2>&1")
                    env.DO_RELEASE = (code != 0).toString()
                    echo "Version ${env.MOSAIC_VERSION}: " + (env.DO_RELEASE == 'true'
                        ? 'no release yet — will package and publish.'
                        : 'release already exists — skipping publish.')
                }
            }
        }

        stage('Package installer') {
            when { expression { env.IS_MASTER == 'true' && env.DO_RELEASE == 'true' } }
            steps {
                // Reuses the existing packaging script verbatim: self-contained win-x64 publish ->
                // Inno Setup -> MosaicSetup-<version>.exe + .sha256. It fails fast with clear
                // guidance if the installer toolchain (ISCC.exe) is missing. Passing -Version
                // guarantees the asset name matches what the Publish stage uploads. Invoked via
                // `bat` (core) so no PowerShell-step plugin is required; package.ps1's non-zero exit
                // (missing toolchain / failed publish) propagates as a cmd errorlevel and fails the stage.
                bat "powershell -NoProfile -ExecutionPolicy Bypass -File installer\\package.ps1 -Version ${env.MOSAIC_VERSION}"
            }
        }

        stage('Publish release') {
            when { expression { env.IS_MASTER == 'true' && env.DO_RELEASE == 'true' } }
            steps {
                // Tag + release + both assets in a single call, so there is no window where the
                // tag exists with missing/partial assets (which auto-update would ignore).
                bat "gh release create v${env.MOSAIC_VERSION} \"installer\\dist\\MosaicSetup-${env.MOSAIC_VERSION}.exe\" \"installer\\dist\\MosaicSetup-${env.MOSAIC_VERSION}.exe.sha256\" --target ${env.GIT_COMMIT} --title \"Mosaic ${env.MOSAIC_VERSION}\" --generate-notes"
                echo "Published release v${env.MOSAIC_VERSION}."
            }
        }
    }

    post {
        success { echo "OK: build/test passed for ${env.GIT_COMMIT}." }
        failure { echo "FAILED: ${env.GIT_COMMIT}." }
        always  { echo "Summary — master=${env.IS_MASTER}, version=${env.MOSAIC_VERSION ?: 'n/a'}, willPublish=${env.DO_RELEASE ?: 'n/a'}." }
    }
}
