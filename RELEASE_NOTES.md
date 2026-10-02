## NKit v3.1.0

### New Features

- **WiiU WUA support** — reads and writes WUA (`.wua`) ZArchive images. Convert WUD/WUX to WUA, or WUA back to WUD/WUX/AppTmd. WUA is a compressed ZArchive container storing decrypted WiiU content in `code/content/meta` layout
- **WiiU Loadiine support** — reads Loadiine folder sources (decrypted `code/content/meta` directory tree) and converts to WUA, AppTmd or other WiiU formats
- **WUA ↔ Loadiine conversion** — bidirectional conversion between WUA and Loadiine formats
- **WUA/Loadiine → AppTmd conversion** — converts decrypted WUA or Loadiine content to installable NUS AppTmd packages (verified working on hardware with Cocoto Magic Circus 2 EU)
- **ZArchive container support** — full ZArchive read/write pipeline integration
- **Folder-format pipeline integration** — WUA and Loadiine sources work with all NKit tasks (scan, extract, convert, dedupe)

### Bug Fixes

- **PS2 CUE+BIN non-standard sector bytes not restored on verify** — Mode2 sectors with non-standard intermediate bytes (`0x10..0x17`) are now detected, packed and restored correctly during NKDS round-trip. Affected images such as *Spielbare Cheats* now verify successfully
- **PS2 integer overflow crash on malformed PVD** — FstContext no longer overflows when the PVD reports an unrealistically large sector count; a 64MB cap prevents the crash seen with images such as *Swap Magic 3*
- **PS2 FileSystem directory records overwriting File data** — ImageBuilder now pre-computes File offset ranges and skips FileSystem writes that overlap an already-committed File area, fixing corruption seen when reconstructing *Swap Magic 3*
- **OffsetsManager BlockPadding stride conversion** — BlockPadding records store raw metadata bytes, not user data, so the stride size conversion must not be applied when building segment intersections. Fixes NKDS verify failures for images with sector padding
- **BlockWriter threading deadlock and polling** — `WaitAll` replaced a 100 ms polling loop with a `ManualResetEventSlim` event, eliminating both the CPU spin and the theoretical deadlock where `_bufferSemaphore.Wait()` was called while `lockObj` was held. The semaphore is now always acquired after the per-key lock is released

### Internal

- `WiiUAppTmdBuilder`, `WiiUFstBuilder` — AppTmd content packaging pipeline
- `WiiUFileExtractBase` — shared extraction engine for all WiiU source types
- `FolderFilesAsIso`/`FolderFilesImage` — folder-format pipeline integration
- `InspectWua` and `RepackAppTmd` diagnostic tools added

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
