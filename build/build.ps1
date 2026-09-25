# NKit Unified Build Script (Windows)
# Usage: build.ps1 -Runtime <rid> [-Config Release|Debug] [-Apps console,gui,nkds,nkds-ui] [-Version X.Y.Z] [-NoArchive]
#
# Examples:
#   .\build\build.ps1 -Runtime win-x64 -Apps console,nkds
#   .\build\build.ps1 -Runtime win-x64 -Apps console,gui,nkds,nkds-ui
#   .\build\build.ps1 -Runtime win-arm64 -Apps gui,nkds-ui
#   .\build\build.ps1 -Runtime win-x64 -Version 2.0.0

param(
    [Parameter(Mandatory=$true)]
    [string]$Runtime,

    [string]$Config = "Release",
    [string]$Apps = "console,gui,nkds,nkds-ui",
    [string]$Version = "",
    [switch]$NoArchive
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
Set-Location $RepoRoot

$TargetFramework = "net10.0"
$GrindCoreNativeDir = ".grindcore-native"

# Parse apps
$AppList = $Apps -split ','
$BuildConsole = $AppList -contains "console"
$BuildGui     = $AppList -contains "gui"
$BuildNkds    = $AppList -contains "nkds"
$BuildNkdsUi  = $AppList -contains "nkds-ui"

# Project paths
$ConsoleProject = "NKitApp/NKitApp.csproj"
$GuiProject     = "NKit.Ui/NKit.Ui.csproj"
$NkdsProject    = "NKDSApp/NKDSApp.csproj"
$NkdsUiProject  = "NKDS.UI/NKDS.UI.csproj"

$env:MATRIX_RUNTIME = $Runtime

Write-Host "=== NKit Build (Windows) ===" -ForegroundColor Cyan
Write-Host "Runtime: $Runtime"
Write-Host "Configuration: $Config"
Write-Host "Apps: $Apps"
if ($Version) { Write-Host "Version: $Version" }
Write-Host ""

# --- Restore ---
Write-Host "--- Restoring dependencies ---" -ForegroundColor Yellow
if ($BuildConsole) { dotnet restore $ConsoleProject --runtime $Runtime }
if ($BuildGui)     { dotnet restore $GuiProject --runtime $Runtime }
if ($BuildNkds)    { dotnet restore $NkdsProject --runtime $Runtime }
if ($BuildNkdsUi)  { dotnet restore $NkdsUiProject --runtime $Runtime }

# --- Inject GrindCore static library ---
# The AOT link needs the static GrindCore.lib in the referenced package's native folder. The
# GrindCore version MUST match the <PackageReference> version (hardcoding it drifts — e.g. it was
# pinned to 0.8.0 while the projects moved to 0.9.0, so the injected lib landed in the wrong folder
# and the link silently fell back to the dynamic .dll). Derive it from the csproj instead.
$StaticLibName = "GrindCore.lib"
$SourceFile = "$GrindCoreNativeDir/$Runtime/native/$StaticLibName"

$GrindCoreVersion = $null
foreach ($proj in @($ConsoleProject, $NkdsProject, "NKitDataStore/NKitDataStore.csproj", "NKit/NKit.csproj")) {
    if (Test-Path $proj) {
        $m = Select-String -Path $proj -Pattern 'PackageReference\s+Include="GrindCore"\s+Version="([^"]+)"' | Select-Object -First 1
        if ($m) { $GrindCoreVersion = $m.Matches[0].Groups[1].Value; break }
    }
}
if (-not $GrindCoreVersion) {
    Write-Host "WARNING: could not determine GrindCore version from csproj; skipping static-lib injection."
} elseif (Test-Path $SourceFile) {
    $NugetPackages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { ".nuget/packages" }
    $DestDir = "$NugetPackages/grindcore/$GrindCoreVersion/runtimes/$Runtime/native"
    if (!(Test-Path $DestDir)) {
        New-Item -ItemType Directory -Path $DestDir -Force | Out-Null
    }
    Write-Host "Injecting GrindCore static library (v$GrindCoreVersion): $SourceFile -> $DestDir/$StaticLibName"
    Copy-Item $SourceFile "$DestDir/$StaticLibName" -Force
} else {
    Write-Host "GrindCore static lib not found at $SourceFile (will use dynamic linking)"
}

# --- Publish ---
Write-Host ""
Write-Host "--- Publishing ---" -ForegroundColor Yellow

function Publish-App {
    param([string]$Project, [string]$AppType)

    Write-Host "Publishing $AppType for $Runtime..."
    $publishArgs = @(
        "publish", $Project,
        "--framework", $TargetFramework,
        "--runtime", $Runtime,
        "--configuration", $Config,
        "--self-contained", "true",
        "--property:PublishSingleFile=true",
        "--property:StripSymbols=true",
        "--property:AllowUnsafeBlocks=true",
        "--property:PlatformName=$Runtime",
        "--output", "publish/$AppType/$Runtime"
    )
    if ($Version) {
        $publishArgs += "--property:Version=$Version"
    }
    & dotnet @publishArgs

    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $AppType" }
    Write-Host "Published $AppType -> publish/$AppType/$Runtime/"
}

if ($BuildConsole) { Publish-App $ConsoleProject "console" }
if ($BuildGui)     { Publish-App $GuiProject "gui" }
if ($BuildNkds)    { Publish-App $NkdsProject "nkds" }
if ($BuildNkdsUi)  { Publish-App $NkdsUiProject "nkds-ui" }

# --- Post-processing ---
Write-Host ""
Write-Host "--- Post-processing ---" -ForegroundColor Yellow

# Clean build artifacts from publish output
foreach ($appType in @("console", "gui", "nkds", "nkds-ui")) {
    $dir = "publish/$appType/$Runtime"
    if (Test-Path $dir) {
        Remove-Item "$dir/*.pdb" -ErrorAction SilentlyContinue
        Remove-Item "$dir/*.deps.json" -ErrorAction SilentlyContinue
        Remove-Item "$dir/*.crproj" -ErrorAction SilentlyContinue
        Remove-Item "$dir/rd.xml" -ErrorAction SilentlyContinue
        # GrindCore.dll is statically linked, remove if present
        Remove-Item "$dir/GrindCore.dll" -ErrorAction SilentlyContinue
    }
}

# Add config files
if ($BuildConsole -and (Test-Path "publish/console/$Runtime")) {
    if (Test-Path "NKitApp/nkit.yaml") { Copy-Item "NKitApp/nkit.yaml" "publish/console/$Runtime/" }
    if (Test-Path "NKit/defaults")     { Copy-Item "NKit/defaults" "publish/console/$Runtime/" -Recurse -Force }
    if (Test-Path "NKitApp/ConfigInfo.txt") { Copy-Item "NKitApp/ConfigInfo.txt" "publish/console/$Runtime/" }
}
if ($BuildNkds -and (Test-Path "publish/nkds/$Runtime")) {
    # nkds uses the same nkit.yaml and defaults/ as the nkit CLI so EnsureConfiguration can
    # copy the default config and fix files into the user-area directory on first run.
    if (Test-Path "NKitApp/nkit.yaml") { Copy-Item "NKitApp/nkit.yaml" "publish/nkds/$Runtime/" }
    if (Test-Path "NKit/defaults")     { Copy-Item "NKit/defaults" "publish/nkds/$Runtime/" -Recurse -Force }
    if (Test-Path "NKitApp/ConfigInfo.txt") { Copy-Item "NKitApp/ConfigInfo.txt" "publish/nkds/$Runtime/" }
}
if ($BuildGui -and (Test-Path "publish/gui/$Runtime")) {
    if (Test-Path "NKit.Ui/nkit-ui.yaml") { Copy-Item "NKit.Ui/nkit-ui.yaml" "publish/gui/$Runtime/" }
    if (Test-Path "NKit/defaults")         { Copy-Item "NKit/defaults" "publish/gui/$Runtime/" -Recurse -Force }
    if (Test-Path "NKit.Ui/ConfigInfo.txt") { Copy-Item "NKit.Ui/ConfigInfo.txt" "publish/gui/$Runtime/" }
}

# --- Archive ---
if (-not $NoArchive) {
    Write-Host ""
    Write-Host "--- Creating archives ---" -ForegroundColor Yellow
    $ArchiveLabel = if ($Version) { $Version } else { Get-Date -Format "yyyyMMdd" }

    # Ensure build-output/ exists alongside build/ so all zips land in one place
    $OutDir = Join-Path (Split-Path -Parent $ScriptDir) "build-output"
    if (!(Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

    # Use 7-Zip for password-protected archives. Compress-Archive does not support passwords.
    $7z = $null
    foreach ($candidate in @("7z", "D:\Src\NKitCode\7z.exe", "C:\Program Files\7-Zip\7z.exe", "C:\Program Files (x86)\7-Zip\7z.exe")) {
        if (Get-Command $candidate -ErrorAction SilentlyContinue) { $7z = $candidate; break }
        if (Test-Path $candidate) { $7z = $candidate; break }
    }
    if (-not $7z) { throw "7-Zip not found. Expected at D:\Src\NKitCode\7z.exe or on PATH." }

    function New-PasswordZip {
        param([string]$SourceDir, [string]$ZipPath, [string]$Password)
        if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
        # Resolve to absolute path before changing directory, then push into source dir
        # so 7-Zip stores bare filenames with no publish/... path prefix in the archive.
        $absZip = [System.IO.Path]::GetFullPath($ZipPath)
        Push-Location $SourceDir
        try {
            & $7z a -tzip -p"$Password" "$absZip" "*" | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "7-Zip failed for $absZip" }
        } finally {
            Pop-Location
        }
        Write-Host "Created: $ZipPath"
    }

    if ($BuildConsole) {
        $cliZip = Join-Path $OutDir "NKit_${Runtime}_CLI_${ArchiveLabel}.zip"
        $cliDir = "publish/console/$Runtime"

        # Include nkds in console archive
        if ($BuildNkds -and (Test-Path "publish/nkds/$Runtime/nkds.exe")) {
            Copy-Item "publish/nkds/$Runtime/nkds.exe" "$cliDir/"
        }

        New-PasswordZip -SourceDir $cliDir -ZipPath $cliZip -Password "nkit"
    }

    if ($BuildGui) {
        $uiZip = Join-Path $OutDir "NKit_${Runtime}_UI_${ArchiveLabel}.zip"
        $guiDir = "publish/gui/$Runtime"

        # Include nkds-ui in gui archive
        if ($BuildNkdsUi -and (Test-Path "publish/nkds-ui/$Runtime/nkds-ui.exe")) {
            Copy-Item "publish/nkds-ui/$Runtime/nkds-ui.exe" "$guiDir/"
        }

        New-PasswordZip -SourceDir $guiDir -ZipPath $uiZip -Password "nkit"
    }
}

Write-Host ""
Write-Host "=== Build complete ===" -ForegroundColor Green
