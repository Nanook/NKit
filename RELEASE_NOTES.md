## NKit v3.0.0

### New async processing core

The engine that reads, processes, and writes disc images has been rebuilt from the ground up. Every image streams as ordered sections through a parallel pipeline with an autoscaling worker pool.

- **Reads directly from archives** — no temporary extraction step for RVZ, WBFS, CISO, CHD, WIA
- **Fewer passes, fewer SSD writes** — GameCube/Wii `.nkit.iso`/`.nkit.gcz` and XBox-in-archive process in a single pass
- **Live progress** with percentage, throughput, and elapsed time shown while a task runs
- **New caching stream layer** — a shared forward-only cache-backed stream enables archive processing without expanding first

### NKit DataStore (`nkds`) — changes since v2.1.0

- **Duplicate-name tagging uses `{00}`** instead of `(00)` — avoids clashes with real parenthesised names like region tags `(USA)`
- **Renaming to dat names** removes duplicates when an entry with the same name and checksums already exists
- **Stored filenames keep their extensions** — no longer stripped on import
- **WiiU** handling improvements: CDN title renaming, kiosk (CAT-I) discs, and other edge cases store and verify correctly
- **DataStore integrity** hardened against interrupted operations
- **PS2 multi-track CUE+BIN** images now store and verify correctly — Track 2 data was previously silently discarded (see Bug fixes)

Supports all NKit systems: Wii, GameCube, WiiU, Xbox, Xbox360, PS1, PS2, PS3, PSP, Dreamcast, Saturn, SegaCD, PC Engine, and more.

See the [DataStore documentation](https://github.com/Nanook/NKit/wiki/NKit-DataStore) for setup and usage.

---

### CLI changes

The command-line interface has been overhauled for v3. Tooling or scripts written for v2 may need updating.

- **Verb-based CLI** — task is the command (`nkit convert …`, `nkit scan …`) with `--long` / `-short` options and a git-style help tree; config-file keys share the CLI names
- **Interactive mode** — `nkit` with no arguments (or a file dropped onto it) launches a guided builder with config-seeded defaults; it shows the equivalent command line so it doubles as a way to learn the CLI
- **Console colours** — colour-coded terminal output for easier reading at a glance
- **Detailed logging** — configurable log levels (`--console-level`, `--log-level`) with `none`, `info`, `detail`, `error`, and `debug` options; useful for diagnosing issues or scripting
- **Per-system option scoping** — options scoped per system on the command line using a `system:` prefix, e.g. `--format wii:rvz:19 --gamecube:wbfs:y`
- **Dat sets** — `--dats-redump`, `--dats-nointro`, `--dats-tosec` accept full Redump/No-Intro/TOSEC collection folders; the correct per-system dat is selected automatically
- **New options** — `--results-out`, `--no-archives`, `--no-config`, `--skip-if-completed`
- **Renamed options** — `--in` → `--input`, `--out` → `--output`. Legacy names are still accepted

### Config file changes (`nkit.yaml`)

Config keys have been renamed to match the new CLI long option names. Legacy key names from v2 are still accepted.

- `in` → `input`, `out` → `output`, `outAsDatMatch` → `dat-match`, `r` → `recursive`
- New keys: `dedupe`, `dats.redump`, `dats.nointro`, `dats.tosec`, `console-level`, `log-level`, `results-out`
- **Portable mode** — if `nkit.yaml` sits next to the executable, all relative paths resolve locally; otherwise `%APPDATA%\nkit`, `~/.config/nkit`, or `~/Documents/nkit` is used
- **First-run setup** — both `nkit` and `nkds` create the full folder tree (`dats/`, `keys/`, `fix/`, `out/`, `logs/`, `temp/`, `dedupe/`) and copy the default config on first run

### New scan format

- Scans are now written as **`<name>.nkit.yaml`** — a compact, query-friendly YAML layout with optional verbose mode. Older `.nkit` XML scans are still read.

### New / expanded format support

- **WIA** — LZMA, LZMA2, uncompressed, and PURGE encodings
- **RVZ, WBFS, CISO, CHD, WIA** — all work when read from inside an archive
- **Wii/GameCube fix files** — each recovery file may be individually compressed in its own archive

---

### Breaking changes

None. All v2 CLI flags and config key names continue to work without modification.

---

### Bug fixes

- Fix Linux ARM64 legacy build — updated to Ubuntu 20.04 toolchain (replaces dead Ubuntu 18.04 LLVM PPA)
- SSBB and other truncated/oversized images process correctly
- CDXA discs are detected more reliably
- cue/bin and CHD Mode 2 images round-trip correctly in both NKit and NKDS
- ISO multisession detection improved; ISO9660 validation more efficient

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
