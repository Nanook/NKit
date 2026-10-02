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

All binaries are self-contained AOT-compiled native executables. No .NET runtime required.

For documentation and setup instructions, see the [Wiki](https://github.com/Nanook/NKit/wiki).
For recent changes and known issues, see [Current Status](https://github.com/Nanook/NKit/wiki/Current-Status).
