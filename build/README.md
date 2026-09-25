# build

Build scripts for local and CI release builds. `build-all.ps1` is the single
entry point that produces all release archives in `<repo-root>/build-output/`
(gitignored, sits next to this folder).

## How it fits together

```
build/build-all.ps1          ← run this
    │
    ├── Windows ── build/build.ps1 -Runtime win-x64
    │                   (same script the CI uses; runs natively on Windows)
    │
    └── Linux ──── build/wsl-docker-build.sh
                       └── build/build-linux-x64.sh
                               └── Docker container running build/container-build.sh
                                       (builds on native ext4; same dotnet publish flags
                                        as build/build.sh, with GrindCore static-link)
```

Both paths produce identical archive layouts — CLI zip (nkit+nkds+config) and UI
zip (nkit-ui+nkds-ui+config) — and both land in `build-output/`.

## Quick start

From Windows PowerShell:

```powershell
pwsh -File build\build-all.ps1 -Version 3.0.0-alpha.3
```

Output in `<repo-root>/build-output/` (gitignored, next to `build/`):

```
NKit_win-x64_CLI_3.0.0-alpha.3.zip    # nkit.exe  + nkds.exe   + config
NKit_win-x64_UI_3.0.0-alpha.3.zip     # nkit-ui.exe + nkds-ui.exe + config
NKit_linux-x64_CLI_3.0.0-alpha.3.zip  # nkit     + nkds       + config
NKit_linux-x64_UI_3.0.0-alpha.3.zip   # nkit-ui  + nkds-ui    + libSkiaSharp.so + libHarfBuzzSharp.so + config
```

Skip one platform if needed:

```powershell
pwsh -File build\build-all.ps1 -Version 3.0.0-alpha.3 -SkipLinux    # Windows only
pwsh -File build\build-all.ps1 -Version 3.0.0-alpha.3 -SkipWindows  # Linux only
```

## Why Linux needs Docker

Building from the `/mnt/d` 9p mount (WSL's view of `D:\`) fails with
`NETSDK1064: Package GrindCore not found` on multi-project self-contained RID
publish — even with a clean cache. This is a known limitation of the WSL 9p
filesystem. CI works because it checks out onto native ext4.

The Docker container copies the source tree to native ext4 inside the container
and builds there. The NuGet cache lives in a persistent Docker volume (`nkit_ngc`)
so subsequent builds are fast.

Additionally:
- **Docker Desktop drops dot-folders** (`.nuget`) on `/mnt` bind mounts — use the
  native WSL `dockerd`, not Docker Desktop. `wsl-docker-build.sh` handles this.
- **GrindCore is AOT-static-linked** via a `LinkerArg` in the csproj. The static
  `.a` is injected from `.grindcore-native/` into the container NuGet cache before
  publishing, mirroring the CI `inject_grindcore` step.

## Options for the Linux build (env vars passed to wsl-docker-build.sh)

| Var | Default | Meaning |
|-----|---------|---------|
| `VERSION` | *(none)* | Version stamp, e.g. `3.0.0-alpha.3` |
| `RUNTIME` | `linux-x64` | Target RID |
| `CONFIG` | `Release` | Build configuration |
| `GC_ZIP_URL` | *(auto)* | Explicit `grindcore.zip` URL to skip the GitHub API lookup |
| `IMAGE` | `nkit-legacy-x64-builder:latest` | Docker builder image tag |

## Runtime notes

- Linux binaries target Ubuntu 18.04 / glibc 2.27 for broad compatibility.
- `nkit` and `nkds` AOT-static-link GrindCore — no `.so` needed at runtime.
- `nkit-ui` and `nkds-ui` ship `libSkiaSharp.so` and `libHarfBuzzSharp.so`
  alongside the binary; Avalonia loads them at startup on Linux.
- All binaries require `libicu` (standard on desktop Linux distros).
