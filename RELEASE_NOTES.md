## NKit v3.2.2

### Bug Fixes

- **GC NKit ISO read corruption** - Some discs decoded incorrectly when the relocated-DOL path fired against a junk-encoded FST file at the same compact-source position, corrupting the entire decoded layout. `emitRelocatedDol` now checks the FST file list first; `RegisterRelocatedDol` is skipped for NKit-decoded sources
- **Pipeline hang on slow or network-backed storage** - `async Task.Run` pipeline stages blocked thread-pool threads when DataStore held a concurrent lock, stalling until the lock released. All stages now use dedicated `LongRunning` threads with `BlockingCollection<T>` queues, eliminating the deadlock
- **Pipeline hang - GC compaction inlining** - Background GC compaction could inline the pipeline's final continuation on a GC worker thread, preventing `completed.CompleteAdding()` from firing. A `Task.Yield()` before `FinalizeAsync` ensures the notification runs on a real thread-pool thread
- **NKDS add hang before verify** - `BlockWriter.InsertBlockWithCompression` had no `CancellationToken` on its semaphore wait, blocking indefinitely if all compression slots were occupied when cancellation was requested
- **Linux system install - config and persistence** - Config directory writability guard was missing; settings changes were not persisted correctly; CSD title was not updating after save
- **macOS NKDS bundle** - Bundle named `NkdsUi.app` instead of `NKDS.app`; `nkds-ui.yaml` missing from macOS packages; zip folder layer incorrect
- **CHD damaged hunk crash** - NKit no longer crashes on CHD files containing a damaged or unreadable hunk
- **RVZ conversion thread leak** - `CircularSequenceQueue` threads were never disposed, causing ~17 leaked threads per image in batch processing
- **NKDS CD sector typing - Mode 1 areas** - Sectors in Mode 1 tracks with a coincidental `0x02` mode byte were misclassified as Mode 2, causing 8 bytes per sector to return as zeros on export
- **NKDS Wii formatter null dereference** - Latent null dereference in the Other-area item selection predicate; no current input triggered it but scan-file reconstruction would crash
- **NKDS expand - CSO-stored images** - PS2 DVD ISOs stored as CSO could not be exported; routing now also matches `cso`, `cso2`, and `zso`
- **NKDS expand - non-standard CUE+BIN** - Discs with hidden data beyond the last declared CUE track crashed on export with `IndexOutOfRangeException`; extra data is now preserved losslessly
- **NKDS exit codes** - `nkds add` and `nkds export` returned exit 0 on `VerifyFailed`; error detection now matches `nkds verify` behaviour
- **NKDS stats - image count always zero** - `nkds stats`/`nkds sets` always showed `Images: 0` and `Total Size: 0 B`; display-only defect, all image records were intact

All binaries are self-contained AOT-compiled native executables. No .NET runtime required.

For documentation and setup instructions, see the [Wiki](https://github.com/Nanook/NKit/wiki).
For recent changes and known issues, see [Current Status](https://github.com/Nanook/NKit/wiki/Current-Status).
