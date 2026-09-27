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
