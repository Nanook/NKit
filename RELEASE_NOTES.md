## NKit v3.2.0

### New Features

- **GameCube nkit.iso conversion** — full lossless GameCube NKit ISO support. Converts any GameCube source to `.nkit.iso` with byte-identical output matching NKit v1. Handles relocated DOLs, zero-size FST files, and custom disc layouts. The canonical format name is `nkitiso`; the `.nkit.iso` file extension alias continues to be accepted
- **WiiU dual-format config** — WiiU image format and index format can now be configured independently. Set `image=wux` and `index=app`/`wua`/`loadiine` in any combination rather than being forced to a single format pair
- **ZArchive parallel compression** — WUA output now uses thread-local ZStd compressor contexts, enabling parallel per-block compression and significantly reducing conversion time on multi-core systems

### Bug Fixes

- **Wii NKit ISO restore** — corrected two distinct categories of decode bug affecting a range of Wii images. Four NKit v1 protocol bugs affecting data-partition junk context, FST file sizes for junk-encoded entries, and gap source-cursor synchronisation. A separate wiped-partition decode bug where partitions with a null game ID were decoded with the wrong NKit source layout, corrupting all subsequent data; this affected Wii system and install disc types that use the null-ID partition encoding
- **ISO9660 DataStore round-trip** — fixed two round-trip failures for ISO9660 discs containing gap content before the FileSystem section, affecting PS2, PSP, and other CD/DVD-based systems
- **PS3 Extract: file size overflow** — `Buffer.TestRangeBounds` overflow caused inflated file sizes to be reported during PS3 disc extraction
- **UDF file size** — `ExtentLength` could exceed `InformationLength` on some UDF volumes, producing oversized reads
- **WUX memory usage** — fixed unbounded `ImageBlockReader` cache growth during WUX-to-WUA conversion
- **Thread-pool starvation and deadlocks** — eliminated thread-pool starvation under sustained load; fixed `BlockingThreadQueue` deadlock; fixed spurious debugger exceptions on task cancellation
- **Memory and handle leaks** — fixed thread, file-handle, and memory leaks at end-of-image-processing and on error paths; temp directory is now cleaned up on error for index-format output
- **Name sanitization** — `CleanseFileName` now substitutes `:` with `-` before the general invalid-character sweep, so dat descriptions such as `Title: Subtitle` produce the more readable `Title- Subtitle`. NKDS name sanitization now delegates to this shared method
- **VFS name safety** — fixed name disambiguation dot-splitting and VFS mount name character validation

All binaries are self-contained AOT-compiled native executables. No .NET runtime required.

For documentation and setup instructions, see the [Wiki](https://github.com/Nanook/NKit/wiki).
For recent changes and known issues, see [Current Status](https://github.com/Nanook/NKit/wiki/Current-Status).
