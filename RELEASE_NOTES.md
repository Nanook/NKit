## NKit v3.1.0

### New Features

- **WiiU WUA support** — reads and writes WUA (`.wua`) ZArchive images. Convert WUD/WUX to WUA, or WUA back to WUD/WUX/AppTmd. WUA is a compressed ZArchive container storing decrypted WiiU content in `code/content/meta` layout
- **WiiU Loadiine support** — reads Loadiine folder sources (decrypted `code/content/meta` directory tree) and converts to WUA, AppTmd or other WiiU formats
- **WUA ↔ Loadiine conversion** — bidirectional conversion between WUA and Loadiine formats
- **WUA/Loadiine → AppTmd conversion** — converts decrypted WUA or Loadiine content to installable NUS AppTmd packages (verified working on hardware)
- **ZArchive container support** — full ZArchive read/write pipeline integration
- **Folder-format pipeline integration** — WUA and Loadiine sources work with all NKit tasks (scan, extract, convert, dedupe)

### Bug Fixes

- **NKDS — ISO9660 non-standard sector bytes not restored on verify** — Mode2 sectors with non-standard intermediate bytes are now correctly packed and restored
- **NKDS — ISO9660 integer overflow crash on malformed PVD** — a 64MB FST buffer cap prevents a crash on discs with unrealistically large sector counts in the PVD
- **NKDS — ISO9660 FileSystem records overwriting File data** — ImageBuilder now skips FileSystem writes that overlap an already-committed File area, fixing reconstruction corruption
- **NKDS — BlockPadding stride conversion** — BlockPadding records are no longer passed through the stride size conversion, fixing verify failures for images with sector padding
- **NKDS — BlockWriter threading** — `WaitAll` now uses event signalling instead of a 100 ms polling loop; the semaphore is acquired after the per-key lock is released, removing a potential deadlock

### Internal

- `WiiUAppTmdBuilder`, `WiiUFstBuilder` — AppTmd content packaging pipeline
- `WiiUFileExtractBase` — shared extraction engine for all WiiU source types
- `FolderFilesAsIso`/`FolderFilesImage` — folder-format pipeline integration
- `InspectWua` and `RepackAppTmd` diagnostic tools added

---

### Downloads

| Package | Platform |
|---|---|
| `NKit_win-x64_CLI_{version}.zip` | Windows x64 |
| `NKit_win-arm64_CLI_{version}.zip` | Windows ARM64 |
| `NKit_linux-x64_CLI_{version}.zip` | Linux x64 |
| `NKit_linux-arm64_CLI_{version}.zip` | Linux ARM64 |
| `NKit_linux-legacy-x64_CLI_{version}.zip` | Linux x64 (legacy glibc, Ubuntu 18.04+) |
| `NKit_linux-legacy-arm64_CLI_{version}.zip` | Linux ARM64 (legacy glibc, Ubuntu 18.04+) |
| `NKit_macOS-Intel_CLI_{version}.zip` | macOS Intel |
| `NKit_macOS-AppleSilicon_CLI_{version}.zip` | macOS Apple Silicon |
| `NKit_*_UI_{version}.zip` | GUI equivalents of all of the above |

All binaries are self-contained AOT-compiled native executables. No .NET runtime required.

For documentation and setup instructions, see the [Wiki](https://github.com/Nanook/NKit/wiki).
For recent changes and known issues, see [Current Status](https://github.com/Nanook/NKit/wiki/Current-Status).

---

## NKit v3.0.1

### Bug fixes

- **PS2 NKDS VerifyFailed for mixed Mode2 CUE+BIN images** — PS2 disc images using mixed Mode2 tracks in CUE+BIN format now store and verify correctly
- **PS2 NKDS VerifyFailed: uncovered FS gaps missed** — a stale parallel-stage FST caused newly stored gaps to be missed, producing false verification failures
- **ISO9660 FST gap analysis regression (v3.0.0)** — directory entries resolved via the refactored `processDirectory` path were incorrectly included in gap analysis; now correctly skipped
- **Xbox beta disc support (PR #10, thanks [@Deterous](https://github.com/Deterous))** — adds recognition of a known XGD1 beta disc (slightly larger than standard OG discs) and a known XGD3 beta disc (same size as XGD3v0 but with different video ISO lengths)

---

### Downloads

| Package | Platform |
|---|---|
| `NKit_win-x64_CLI_{version}.zip` | Windows x64 |
| `NKit_win-arm64_CLI_{version}.zip` | Windows ARM64 |
| `NKit_linux-x64_CLI_{version}.zip` | Linux x64 |
| `NKit_linux-arm64_CLI_{version}.zip` | Linux ARM64 |
| `NKit_linux-legacy-x64_CLI_{version}.zip` | Linux x64 (legacy glibc, Ubuntu 18.04+) |
| `NKit_linux-legacy-arm64_CLI_{version}.zip` | Linux ARM64 (legacy glibc, Ubuntu 18.04+) |
| `NKit_macOS-Intel_CLI_{version}.zip` | macOS Intel |
| `NKit_macOS-AppleSilicon_CLI_{version}.zip` | macOS Apple Silicon |
| `NKit_*_UI_{version}.zip` | GUI equivalents of all of the above |

All binaries are self-contained AOT-compiled native executables. No .NET runtime required.

For documentation and setup instructions, see the [Wiki](https://github.com/Nanook/NKit/wiki).
For recent changes and known issues, see [Current Status](https://github.com/Nanook/NKit/wiki/Current-Status).
