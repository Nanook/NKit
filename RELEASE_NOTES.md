## NKit v3.2.2

### Bug Fixes

- **CHD damaged hunk crash** — NKit no longer crashes when processing a CHD file containing a damaged or unreadable hunk. The affected hunk is flagged and processing continues where possible
- **RVZ conversion thread leak** — `CircularSequenceQueue` threads were never disposed after RVZ conversion completed, causing thread accumulation when processing large batches. At ~17 leaked threads per image, a batch of 1,000 would exhaust the thread pool. Fixed by disposing the queue on completion
- **NKDS CD sector typing — Mode 1 areas** — `SectorPaddingPacker` unconditionally typed sectors by their own mode byte. Some discs (e.g. Sega CD coverdiscs such as Silpheed, Datel GC discs) have trailing unstructured data where byte `0x0F` happens to be `0x02` by coincidence — not a real Mode 2 sector. These were misclassified as Mode 2, causing bytes `0x810–0x817` to be omitted from the stored stride and pack payload, returning as zeros on export. Mode 1 areas now force all sectors to Mode 1 regardless of mode byte
- **NKDS Wii formatter null dereference** — wrong operator precedence in the Other-area item selection predicate could dereference a null `Gap` reference. No current input triggered it, but items rebuilt from a scan file would crash on import
- **NKDS expand — CSO-stored images** — PS2 DVD ISOs are stored as CSO (the system default format). The `Expand-Image` task routing only matched `Config: iso/`, so CSO-stored images hit `NotSet-NotSupported` on export. The routing now also matches `cso`, `cso2`, and `zso`
- **NKDS expand — non-standard CUE+BIN discs** — Datel/Action Replay PS2 discs and similar that declare one CUE track but contain additional data beyond the last declared track boundary crashed with `IndexOutOfRangeException` during export. Data beyond the last declared track is now preserved losslessly in the last track's output file
- **NKDS exit codes** — `nkds add` and `nkds export` returned exit 0 and printed "Imported/Exported N of N" even when the operation failed with `VerifyFailed`. The error counter now correctly detects both `ErrorMsg` and `VerifyResult` failure states, matching the exit code behaviour of `nkds verify` and the NKit CLI
- **NKDS stats — image count always zero** — `nkds stats` / `nkds sets` always showed `Images: 0` and `Total Size: 0 B` regardless of set contents. `DescribeSets()` was calling `GetSetInfo(..., false)` which skips the image directory scan; changed to `true`. The image records were always intact — this was a display-only defect

All binaries are self-contained AOT-compiled native executables. No .NET runtime required.

For documentation and setup instructions, see the [Wiki](https://github.com/Nanook/NKit/wiki).
For recent changes and known issues, see [Current Status](https://github.com/Nanook/NKit/wiki/Current-Status).
