# =============================================================================
# NKit build launcher — Windows + Linux, all outputs to build-output/
# =============================================================================
# Builds both win-x64 and linux-x64 release archives and places them in
# <repo-root>/build-output/ (gitignored).
#
#   win-x64  : NKit_win-x64_CLI_VERSION.zip  +  NKit_win-x64_UI_VERSION.zip
#   linux-x64: NKit_linux-x64_CLI_VERSION.zip  +  NKit_linux-x64_UI_VERSION.zip
#              (console + nkds + GUI, via WSL + Docker)
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

# 7-Zip for password-protected archives (Compress-Archive doesn't support passwords)
$7z = @(
    "C:\Program Files\7-Zip-Zstandard\7z.exe",
    "C:\Program Files\7-Zip\7z.exe",
    "C:\Program Files (x86)\7-Zip\7z.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
$ZipPassword = "nkit"

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Write-Host "=== NKit local build — v$Version ===" -ForegroundColor Cyan
Write-Host "Output: $OutDir"
Write-Host ""

# --- Windows build ---
if (-not $SkipWindows) {
    Write-Host "--- Windows x64 ---" -ForegroundColor Yellow
    Push-Location $RepoRoot
    try {
        & ".\build\build.ps1" -Runtime win-x64 -Apps "console,gui,nkds,nkds-ui" -Version $Version
        if ($LASTEXITCODE -ne 0) { throw "Windows build failed (exit $LASTEXITCODE)" }

        # Re-package with 7z (password protection) and move to build-output/
        Get-ChildItem $RepoRoot -Filter "NKit_*_win-x64_*.zip" | ForEach-Object {
            $plainZip = $_.FullName
            $destZip  = Join-Path $OutDir $_.Name

            if ($7z) {
                # Extract flat then re-zip with password via 7z
                $tmpDir = Join-Path $env:TEMP ("nkit_repack_" + [System.IO.Path]::GetRandomFileName())
                New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
                try {
                    & $7z x $plainZip -o"$tmpDir" -y | Out-Null
                    Remove-Item $plainZip -Force
                    & $7z a -tzip -p"$ZipPassword" -mem=AES256 -mx=5 $destZip "$tmpDir\*" | Out-Null
                } finally {
                    Remove-Item -Recurse -Force $tmpDir -ErrorAction SilentlyContinue
                }
            } else {
                # No 7z — move as-is without password
                Write-Warning "7-Zip not found; $($_.Name) will not be password-protected"
                Move-Item -Force $plainZip $destZip
            }
            Write-Host "  -> $($_.Name)"
        }
    } finally {
        Pop-Location
    }
    Write-Host ""
}

# --- Linux build (via WSL + Docker) ---
if (-not $SkipLinux) {
    Write-Host "--- Linux x64 (WSL + Docker) ---" -ForegroundColor Yellow

    # Run via WSL; wsl-docker-build.sh handles native dockerd startup
    $linuxScript = "/mnt/" + ($RepoRoot -replace '\\', '/' -replace '^([A-Za-z]):', '$1').ToLower()
    $wslCmd = "cd $linuxScript && VERSION=$Version ./build/wsl-docker-build.sh"

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
