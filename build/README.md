# build

Build scripts for local and CI release builds. `build-all.ps1` is the single
entry point that produces all release archives in `<repo-root>/build-output/`
(gitignored, sits next to this folder).

## How it fits together

```
build/build-all.ps1          ← run this locally
    │
    ├── Windows ── build\build.ps1 -Runtime win-x64
    │                   Same script CI uses; produces password-protected archives
    │                   in build-output/ directly via 7-Zip.
    │
    └── Linux ──── build/build-wsl.sh --runtime linux-x64
                       Thin wrapper: starts native WSL dockerd, copies only the
                       source files needed to native ext4 inside the container
                       (avoids WSL 9p NuGet failures), then runs the same
                       build/build.sh --legacy-container that CI uses.
```

Both paths produce identical archive layouts - CLI zip (nkit + nkds + config)
and UI zip (nkit-ui + nkds-ui + config) - and both land in `build-output/`.

## Prerequisites

### Windows builds
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (`winget install Microsoft.DotNet.SDK.10`)
- [7-Zip](https://www.7-zip.org/) — required for the password-protected local archives (`winget install 7zip.7zip`)

### Linux builds (from Windows)
- **WSL 2** with Ubuntu 22.04 (`wsl --install -d Ubuntu-22.04`)
- **Native Docker inside WSL** — docker-ce, NOT Docker Desktop. Docker Desktop drops dot-folders (`.nuget`, `.git`) on 9p bind mounts. Install docker-ce inside Ubuntu:
  ```bash
  sudo apt-get update && sudo apt-get install -y docker-ce docker-ce-cli
  ```

## Quick start

From Windows PowerShell:

```powershell
pwsh -File build\build-all.ps1 -Version 3.0.0-alpha.3
```

Output in `<repo-root>/build-output/` (gitignored):

```
NKit_win-x64_CLI_3.0.0-alpha.3.zip         # nkit.exe  + nkds.exe   + config
NKit_win-x64_UI_3.0.0-alpha.3.zip          # nkit-ui.exe + nkds-ui.exe + config
NKit_linux-legacy-x64_CLI_3.0.0-alpha.3.zip # nkit     + nkds       + config
NKit_linux-legacy-x64_UI_3.0.0-alpha.3.zip  # nkit-ui  + nkds-ui    + libSkiaSharp.so + libHarfBuzzSharp.so + config
```

Skip one platform if needed:

```powershell
pwsh -File build\build-all.ps1 -Version 3.0.0-alpha.3 -SkipLinux    # Windows only
pwsh -File build\build-all.ps1 -Version 3.0.0-alpha.3 -SkipWindows  # Linux only
```

## Why Linux needs Docker

Building from the `/mnt/d` 9p mount (WSL's view of `D:\`) fails with
`NETSDK1064: Package GrindCore not found` on multi-project self-contained RID
publish. `build-wsl.sh` works around this by:

1. Running the build inside a Docker container (Ubuntu 18.04 / glibc 2.27).
2. Copying only the needed source files from `/mnt/d` to native ext4 at `/build`
   inside the container before running `build.sh --legacy-container`.
3. Using a persistent Docker volume (`nkit_ngc`) for the NuGet package cache so
   subsequent builds restore quickly without re-downloading.

The `build.sh --legacy-container` call inside the container is the same call
that `build/build-legacy.sh` makes on GitHub Actions, so local and CI builds
share the same build logic.

## Options for the Linux build (args to build-wsl.sh)

| Arg | Default | Meaning |
|-----|---------|---------|
| `--runtime` | `linux-x64` | Target RID (`linux-x64` or `linux-arm64`) |
| `--apps` | `console,gui,nkds,nkds-ui` | Comma-separated apps to build |
| `--config` | `Release` | Build configuration |
| `--version` | *(none)* | Version stamp, e.g. `3.0.0-alpha.3` |
| `--gc-zip-url` | *(auto)* | Explicit `grindcore.zip` URL to skip the GitHub API lookup |

## Runtime notes

- Linux binaries target Ubuntu 18.04 / glibc 2.27 for broad compatibility.
- `nkit` and `nkds` AOT-static-link GrindCore - no `.so` needed at runtime.
- `nkit-ui` and `nkds-ui` ship `libSkiaSharp.so` and `libHarfBuzzSharp.so`
  alongside the binary; Avalonia loads them at startup on Linux.
- All binaries require `libicu` (standard on desktop Linux distros).
