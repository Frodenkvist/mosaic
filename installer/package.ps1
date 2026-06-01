<#
.SYNOPSIS
    Publishes Mosaic (self-contained, win-x64) and compiles the Windows installer.

.DESCRIPTION
    One-command packaging: cleans previous output, runs `dotnet publish` into installer\publish,
    then compiles installer\Mosaic.iss with Inno Setup (ISCC.exe), producing
    installer\dist\MosaicSetup-<version>.exe.

.PARAMETER Version
    Product version to stamp. Defaults to <Version> read from Mosaic.csproj.

.EXAMPLE
    .\installer\package.ps1
    .\installer\package.ps1 -Version 1.2.0
#>
[CmdletBinding()]
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$scriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot   = Split-Path -Parent $scriptDir
$csproj     = Join-Path $repoRoot 'Mosaic.csproj'
$issFile    = Join-Path $scriptDir 'Mosaic.iss'
$publishDir = Join-Path $scriptDir 'publish'
$distDir    = Join-Path $scriptDir 'dist'

# 1. Resolve the version (default: <Version> from Mosaic.csproj — the single source of truth).
if (-not $Version) {
    [xml]$xml = Get-Content -LiteralPath $csproj
    $node = $xml.SelectSingleNode('//PropertyGroup/Version')
    if (-not $node -or -not $node.InnerText.Trim()) {
        throw "Could not read <Version> from $csproj. Pass -Version explicitly."
    }
    $Version = $node.InnerText.Trim()
}
Write-Host "Packaging Mosaic $Version" -ForegroundColor Cyan

# 2. Locate the Inno Setup compiler BEFORE the expensive publish, so a missing toolchain fails fast.
function Find-Iscc {
    $cmd = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidates = @(
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path -LiteralPath $c)) { return $c }
    }
    return $null
}
$iscc = Find-Iscc
if (-not $iscc) {
    Write-Host "ERROR: Inno Setup compiler (ISCC.exe) was not found." -ForegroundColor Red
    Write-Host "Install it, then re-run this script:"
    Write-Host "    winget install --exact --id JRSoftware.InnoSetup --accept-package-agreements --accept-source-agreements"
    Write-Host "Or download it from https://jrsoftware.org/isdl.php"
    exit 1
}
Write-Host "Using ISCC: $iscc"

# 3. Clean previous publish/dist output.
foreach ($dir in @($publishDir, $distDir)) {
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
}

# 4. Self-contained win-x64 publish into installer\publish.
Write-Host "Publishing self-contained win-x64 ..." -ForegroundColor Cyan
& dotnet publish $csproj -c Release -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE)." }

# 5. Compile the installer. OutputDir=dist in the .iss is relative to the script, i.e. installer\dist.
Write-Host "Compiling installer ..." -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$Version" $issFile
if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit code $LASTEXITCODE)." }

$output = Join-Path $distDir "MosaicSetup-$Version.exe"
if (-not (Test-Path -LiteralPath $output)) {
    throw "Expected installer not found at $output."
}
Write-Host "Created $output" -ForegroundColor Green

# 6. Emit a SHA-256 checksum next to the installer. The in-app auto-updater downloads this alongside
#    the installer and verifies the download before running it (builds are unsigned, so this is the
#    integrity check). Format: "<lowercase-hash>  <filename>" (sha256sum-compatible).
$hash       = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
$sha256Path = "$output.sha256"
"$hash  MosaicSetup-$Version.exe" | Set-Content -LiteralPath $sha256Path -Encoding ascii -NoNewline
Write-Host "Created $sha256Path" -ForegroundColor Green
