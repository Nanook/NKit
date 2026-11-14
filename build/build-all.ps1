# =============================================================================
# NKit build launcher — Windows + Linux, all outputs to build-output/
# =============================================================================
# Builds win-x64 and linux-x64 release archives and places them in
# <repo-root>/build-output/ (gitignored).
#
#   win-x64        : NKit_win-x64_CLI_VERSION.zip  +  NKit_win-x64_UI_VERSION.zip
#   linux-legacy-x64: NKit_linux-legacy-x64_CLI_VERSION.zip + ..._UI_VERSION.zip
#              (legacy glibc 2.27 build via WSL + Docker, identical to CI path)
#
# Both platforms now use the same build scripts as GitHub Actions:
#   Windows → build\build.ps1  (without -NoPassword; produces password-protected
#                               archives in build-output/ directly via 7-Zip)
#   Linux   → build/build-wsl.sh  (handles Docker startup + native-ext4 copy,
#                               then delegates to build/build.sh --legacy-container)
#
# Usage:
#   pwsh -File build\build-all.ps1 -Version 3.0.0-alpha.3
#   pwsh -File build\build-all.ps1 -Version 3.0.0-alpha.3 -SkipLinux
#   pwsh -File build\build-all.ps1 -Version 3.0.0-alpha.3 -SkipWindows
# =============================================================================
param(
    [Parameter(Mandatory=$true)]
    [string]$Version,

    [switch]$SkipWindows,
    [switch]$SkipLinux
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir
$OutDir    = Join-Path $RepoRoot 'build-output'

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Write-Host "=== NKit local build — v$Version ===" -ForegroundColor Cyan
Write-Host "Output: $OutDir"
Write-Host ""

# --- Windows build ---
if (-not $SkipWindows) {
    Write-Host "--- Windows x64 ---" -ForegroundColor Yellow
    Push-Location $RepoRoot
    try {
        # build.ps1 without -NoPassword runs in local mode: creates password-protected
        # archives in build-output/ using 7-Zip. No repack step needed.
        & ".\build\build.ps1" -Runtime win-x64 -Apps "console,gui,nkds,nkds-ui" -Version $Version
        if ($LASTEXITCODE -ne 0) { throw "Windows build failed (exit $LASTEXITCODE)" }
    } finally {
        Pop-Location
    }
    Write-Host ""
}

# --- Linux build (via WSL + Docker) ---
if (-not $SkipLinux) {
    Write-Host "--- Linux x64 (WSL + Docker) ---" -ForegroundColor Yellow

    # Convert Windows path to WSL /mnt/ path
    $linuxPath = "/mnt/" + ($RepoRoot -replace '\\', '/' -replace '^([A-Za-z]):', '$1').ToLower()
    $versionArg = if ($Version) { " --version $Version" } else { "" }
    $wslCmd = "cd '$linuxPath' && build/build-wsl.sh --runtime linux-x64 --apps console,gui,nkds,nkds-ui$versionArg"

    Write-Host "  Running: wsl -d Ubuntu-22.04 -- bash -lc `"$wslCmd`""
    wsl -d Ubuntu-22.04 -- bash -lc $wslCmd
    if ($LASTEXITCODE -ne 0) { throw "Linux build failed (exit $LASTEXITCODE)" }
    Write-Host ""
}

# --- Summary ---
Write-Host "=== Build complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "Archives in $OutDir :"
Get-ChildItem $OutDir -Filter "*.zip" | Sort-Object Name | ForEach-Object {
    $mb = [math]::Round($_.Length / 1MB, 1)
    Write-Host "  $($_.Name)  ($mb MB)"
}
