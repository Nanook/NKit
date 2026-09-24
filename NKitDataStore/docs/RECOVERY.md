# Recovery Documentation

This document catalogs every possible failure scenario in NKitDataStore, the intermediate file state each failure produces, how recovery detects that state, and what action is taken. The core invariant is:

> **After recovery, the set is in the state of the last successful AtomicCommit.**

No partially-applied changes from operations that did not complete AtomicCommit are ever visible after recovery.

---

## Table of Contents

- [Dual-Header Protocol](#dual-header-protocol)
- [Recovery Ordering](#recovery-ordering)
- [Recovery State Detection Matrix](#recovery-state-detection-matrix)
- [Failure Scenarios by Operation](#failure-scenarios-by-operation)
  - [AddImage (ImageWriter)](#addimage-imagewriter)
  - [DeleteImage](#deleteimage)
  - [RestoreImage](#restoreimage)
  - [RollbackImage](#rollbackimage)
  - [CompactSetSeparate](#compactsetseparate)
  - [CompactSetEmbedded](#compactsetembedded)
  - [CompactShards Pipeline](#compactshards-pipeline)
- [Summary Table](#summary-table)
- [Unrecoverable States](#unrecoverable-states)
- [Maintenance](#maintenance)

---

## Dual-Header Protocol

The `BinaryIndexFile` uses a dual-header commit protocol to ensure atomicity of all index mutations:

```
┌──────────────────────────────────────────────────────────┐
│ Offset 0x000: PrimaryHeader   (256 bytes, written LAST)  │
│ Offset 0x100: SecondaryHeader (256 bytes, written FIRST) │
│ Offset 0x200: Data region...                             │
└──────────────────────────────────────────────────────────┘
```

### Commit Sequence

1. Flush all appended data to disk
2. Write SecondaryHeader with new state → flush to disk
3. Write PrimaryHeader with new state → flush to disk

### Recovery Rules

| PrimaryHeader | SecondaryHeader | Action |
|---------------|-----------------|--------|
| Valid | Valid (agrees) | Use PrimaryHeader — commit completed normally |
| Valid | Valid (disagrees) | Use PrimaryHeader — it was written last and is authoritative |
| Invalid/corrupt | Valid | Use SecondaryHeader — crash occurred between step 2 and step 3 |
| Invalid/corrupt | Invalid/corrupt | **Unrecoverable** — file is corrupt |

The dual-header protocol guarantees that at least one valid header exists after any single crash during AtomicCommit, because the headers are written sequentially with flushes between them.

---

## Recovery Ordering

When a set is opened, `RecoverIfNeeded` executes recovery operations in strict order:

1. **File-level recovery** — Handle `.compact.tmp` files, shard `.tmp` files, embedded intermediate states
2. **Dual-header recovery** — `BinaryIndexFile.Open` selects the valid header (primary or secondary)
3. **Orphaned tail detection** — Compute expected shard sizes from committed block index, truncate excess
4. **Header validation** — Verify the recovered index passes structural validation
5. **Set available** — Return the set for reads/writes

This ordering ensures that file-level issues are resolved before the index is opened, and orphaned data is cleaned up before the set becomes available.

---

## Recovery State Detection Matrix

| Condition | Status | Recovery Action |
|-----------|--------|-----------------|
| All files consistent, no orphaned data | Clean | None |
| `.compact.tmp` exists + original exists | NeedsRecovery | Delete `.compact.tmp` |
| `.compact.tmp` exists + original missing | NeedsRecovery | Rename `.compact.tmp` → original, validate |
| Shard `.tmp` files exist + originals exist | NeedsRecovery | Delete shard `.tmp` files |
| Embedded intermediate (TryRecover states) | NeedsRecovery | `EmbeddedIndexCommitter.TryRecover` |
| Shard larger than committed offset | NeedsRecovery | Truncate to committed offset |
| Shard exists but no blocks reference it | NeedsRecovery | Delete shard file |
| `.compact.tmp` only + fails validation | Unrecoverable | Report error |
| No index file and no `.compact.tmp` | Unrecoverable | Report error |
| Both primary and secondary headers corrupt | Unrecoverable | Report error |

---

## Failure Scenarios by Operation

### AddImage (ImageWriter)

The ImageWriter appends blocks to shard files, appends metadata sections, then performs AtomicCommit to make the new image visible in the directory.

#### Step 1: Block writes to shard

**Operation:** Append compressed/hashed blocks to the shard file.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| During block append (partial block written) | Shard file larger than committed block index offset; partial block bytes at end | Compare shard file size against max committed offset for that shard in block index | Truncate shard to last committed offset | All previously committed images and their blocks | Partially-written block bytes from the failed AddImage |
| After one or more full blocks appended, before next block | Shard file larger than committed block index offset; one or more complete but uncommitted blocks at end | Compare shard file size against max committed offset for that shard in block index | Truncate shard to last committed offset | All previously committed images and their blocks | All blocks written by the failed AddImage (not yet in block index) |

#### Step 2: Metadata section write

**Operation:** Append image metadata (area offsets, file records) to the shard.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| During metadata append | Shard contains blocks + partial metadata beyond committed offset | Compare shard file size against max committed offset | Truncate shard to last committed offset | All previously committed images | Blocks and metadata from the failed AddImage |

#### Step 3: AtomicCommit

**Operation:** Write SecondaryHeader → flush → Write PrimaryHeader → flush.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| Before SecondaryHeader write | Shard has new data; index headers unchanged | Orphaned tail detection (shard larger than committed offset) | Truncate shard to committed offset | All previously committed images | New image's blocks and metadata |
| After SecondaryHeader write, before PrimaryHeader write | SecondaryHeader has new state; PrimaryHeader has old state | Dual-header recovery: PrimaryHeader invalid or disagrees → use SecondaryHeader | Use SecondaryHeader as authoritative state | All images including the new one (commit succeeded via secondary) | Nothing — the new image is preserved |
| After PrimaryHeader write (commit complete) | Both headers valid with new state | No recovery needed | None | All images including the new one | Nothing |

#### Step 4: After AtomicCommit, before lock release

**Operation:** Release the writer lock.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| After commit, before lock release | Stale writer lock file exists; index is fully committed | Detect stale lock (no active writer process) | Release stale lock on next open | All images including the new one | Nothing — commit was successful |

---

### DeleteImage

DeleteImage updates the image directory to mark an image as removed, then performs AtomicCommit.

#### Step 1: Directory update (mark image as removed)

**Operation:** Append updated directory entry to the index data region.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| During directory append | Index data region has partial directory bytes beyond committed offset | Dual-header recovery: both headers still point to old directory state | Use last valid header; orphaned directory bytes are in unreferenced region | All images retain their prior status (image remains live) | The partial directory update |

#### Step 2: AtomicCommit

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| Before SecondaryHeader write | Directory appended but headers unchanged | Headers point to old directory offset | Use existing headers — old state is authoritative | Image remains live (delete not applied) | The appended directory bytes (unreferenced) |
| After SecondaryHeader, before PrimaryHeader | SecondaryHeader references new directory; PrimaryHeader references old | Dual-header protocol | Use SecondaryHeader — delete is applied | Image is marked as removed | Nothing — operation completed via secondary |
| After PrimaryHeader (complete) | Both headers reference new directory | No recovery needed | None | Image is marked as removed | Nothing |

---

### RestoreImage

RestoreImage updates the image directory to mark a removed image as live, then performs AtomicCommit. The failure pattern is identical to DeleteImage.

#### Step 1: Directory update (mark image as live)

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| During directory append | Partial directory bytes beyond committed offset | Dual-header recovery | Use last valid header | Image remains removed (restore not applied) | The partial directory update |

#### Step 2: AtomicCommit

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| Before SecondaryHeader write | Directory appended but headers unchanged | Headers point to old directory offset | Use existing headers | Image remains removed | Appended directory bytes |
| After SecondaryHeader, before PrimaryHeader | SecondaryHeader references new directory | Dual-header protocol | Use SecondaryHeader — restore is applied | Image is marked as live | Nothing |
| After PrimaryHeader (complete) | Both headers reference new directory | No recovery needed | None | Image is marked as live | Nothing |

---

### RollbackImage

RollbackImage updates the directory to restore all images to their prior removed/live status, then performs AtomicCommit. The failure pattern is identical to DeleteImage/RestoreImage — all-or-nothing semantics via AtomicCommit.

#### Step 1: Directory update (rollback all statuses)

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| During directory append | Partial directory bytes beyond committed offset | Dual-header recovery | Use last valid header | All images retain their current status (rollback not applied) | The partial directory update |

#### Step 2: AtomicCommit

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| Before SecondaryHeader write | Directory appended but headers unchanged | Headers point to old directory offset | Use existing headers | All images retain current status | Appended directory bytes |
| After SecondaryHeader, before PrimaryHeader | SecondaryHeader references rolled-back directory | Dual-header protocol | Use SecondaryHeader — rollback is applied | All images have rolled-back status | Nothing |
| After PrimaryHeader (complete) | Both headers reference rolled-back directory | No recovery needed | None | All images have rolled-back status | Nothing |

---

### CompactSetSeparate

CompactSetSeparate compacts the index by removing entries for deleted images, then atomically replaces the original index file.

#### Step 1: Compact to temp file

**Operation:** Write compacted index to `.compact.tmp`.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| During write to `.compact.tmp` | Partial `.compact.tmp` + intact original index | `.compact.tmp` exists alongside original | Delete `.compact.tmp` | All data in original index (pre-compact state) | Partial `.compact.tmp` |

#### Step 2: Validate compacted temp file

**Operation:** Verify the `.compact.tmp` passes header validation.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| During validation | Complete `.compact.tmp` + intact original index | `.compact.tmp` exists alongside original | Delete `.compact.tmp` | All data in original index | `.compact.tmp` |

#### Step 3: Close old index file

**Operation:** Close file handles to the original index.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| During close | Complete `.compact.tmp` + intact original index | `.compact.tmp` exists alongside original | Delete `.compact.tmp` | All data in original index | `.compact.tmp` |

#### Step 4: Replace original with temp (File.Replace)

**Operation:** Atomically swap `.compact.tmp` into the original path via `File.Replace` (or fallback `File.Delete` + `File.Move`).

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| During `File.Replace` (atomic) | Either original or `.compact.tmp` exists (OS guarantees at least one) | Check which files exist | If both exist: delete `.compact.tmp`. If only `.compact.tmp`: rename to original, validate | Whichever file survived | Nothing — at least one valid index exists |
| During fallback: after `File.Delete`, before `File.Move` | Original deleted; `.compact.tmp` still at temp path | Only `.compact.tmp` exists (original missing) | Rename `.compact.tmp` → original path, validate header | Compacted index (all live images preserved) | Removed image entries (intended to be discarded by compact) |

#### Step 5: Reopen index

**Operation:** Open the replaced index file.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| During reopen | Valid index file at original path (replacement completed) | Normal file exists | Open normally | All live images | Nothing — compact completed |

---

### CompactSetEmbedded

CompactSetEmbedded extracts an embedded index to separate mode, compacts it, then re-embeds it.

#### Step 1: Extract to separate mode

**Operation:** `EmbeddedIndexCommitter` extracts the index from the embedded shard file to a standalone `.tmp` file, then renames.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| After `.tmp` written, before first rename | Standalone `.tmp` file + original embedded file intact | `EmbeddedIndexCommitter.TryRecover` detects `.tmp` with original present | Delete `.tmp`, treat original embedded file as authoritative | All data in original embedded file | The `.tmp` extraction |
| After first rename, before second rename | Files in separated state (index and shard as separate files) | `EmbeddedIndexCommitter.TryRecover` detects separated state | Continue as separate mode (valid state for compaction) | All data (now in separate mode) | Nothing |
| After both renames, before shard truncation | Shard file contains stale index+footer bytes at end | Orphaned tail detection (shard larger than committed offset) | Truncate shard to shard boundary | All block data in shard | Stale index+footer bytes |

#### Step 2: Open standalone index

**Operation:** Open the extracted standalone index file.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| During open | Valid standalone index + shard file (separated state) | Normal separate mode detection | Open as separate mode set | All data | Nothing |

#### Step 3: Compact shards

**Operation:** Run the CompactShards pipeline on the separated set.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| Any point during shard compaction | See [CompactShards Pipeline](#compactshards-pipeline) section | Various (see below) | See CompactShards recovery | All committed data | Partial compact progress |

#### Step 4: Compact index to temp (write `.compact.tmp`)

**Operation:** Write compacted index to `.compact.tmp`.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| During write | Partial `.compact.tmp` + standalone index intact | `.compact.tmp` alongside original | Delete `.compact.tmp` | Standalone index (post-shard-compact state) | Partial `.compact.tmp` |

#### Step 5: Replace standalone index with compacted version (File.Replace)

**Operation:** Atomically swap `.compact.tmp` into the standalone index path.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| During `File.Replace` | Either standalone index or `.compact.tmp` exists | Check which files exist | Same as CompactSetSeparate Step 4 | At least one valid index | Nothing critical |
| During fallback: after delete, before move | Only `.compact.tmp` exists | Only `.compact.tmp` present | Rename to original, validate | Compacted index | Nothing critical |

#### Step 6: Re-embed (merge index back into shard)

**Operation:** `EmbeddedIndexCommitter` appends the index to the shard file, writes footer, then renames.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| After index appended to shard, before footer written | Shard has index bytes but no footer magic | `EmbeddedIndexCommitter.TryRecover` detects missing footer | Re-append footer to complete embedding | All data (index is in shard) | Nothing — footer is re-written |
| After footer written, before rename | Shard has valid footer magic; rename not yet done | `EmbeddedIndexCommitter.TryRecover` detects completed shard | Complete the rename | All data | Nothing |
| After rename (complete) | Fully embedded set | No recovery needed | None | All data | Nothing |

---

### CompactShards Pipeline

The CompactShards pipeline processes shard files to remove unreferenced blocks. It consists of multiple stages executed in sequence, with a final AtomicCommit.

#### Stage 1: GatherReferencedKeys

**Operation:** Scan live images to collect all referenced BlockKeys.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| During scan | No file changes (read-only operation) | No intermediate state on disk | None needed — restart from beginning | All data unchanged | Nothing |

#### Stage 2: BuildChunkList

**Operation:** Classify block index entries as referenced or unreferenced.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| During classification | No file changes (in-memory operation) | No intermediate state on disk | None needed — restart from beginning | All data unchanged | Nothing |

#### Stage 3: GroupChunksByShard

**Operation:** Group classified chunks by their shard file ID.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| During grouping | No file changes (in-memory operation) | No intermediate state on disk | None needed — restart from beginning | All data unchanged | Nothing |

#### Stage 4: CompactShard (per shard)

**Operation:** For each shard with unreferenced blocks, write live chunks to a `.tmp` file, then atomically replace the original shard.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| During `.tmp` write (partial) | Partial shard `.tmp` + intact original shard | Shard `.tmp` exists alongside original | Delete shard `.tmp` file | Original shard (all blocks intact) | Partial `.tmp` |
| During `File.Replace` of shard | Either original shard or `.tmp` exists | Check which files exist | If both: delete `.tmp`. If only `.tmp`: rename to original | At least one valid shard | Nothing critical |
| After replace of one shard, before next shard | Some shards compacted, others not; no AtomicCommit yet | Block index still references old offsets; compacted shards have new layout | On recovery: orphaned tail detection handles size mismatches; next compact will re-process | All block data (offsets may be stale until next compact) | Nothing — data is preserved |

#### Stage 5: DeleteEmptyShards

**Operation:** Delete shard files that have no referenced blocks.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| After deleting some empty shards | Some empty shards deleted, others remain; no AtomicCommit yet | Block index references no blocks in deleted shards | No action needed — shards were empty | All referenced data | Empty shard files (contained only dead blocks) |

#### Stage 6: UpdateBlockIndex

**Operation:** Update block index entries with new offsets from compacted shards, remove unreferenced entries.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| During update | In-memory operation; no disk changes until AtomicCommit | No intermediate state on disk | None needed — restart pipeline | All data unchanged on disk | Nothing |

#### Stage 7: UpdateBlockMaps

**Operation:** Update per-image block maps with new offsets.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| During update | In-memory operation; no disk changes until AtomicCommit | No intermediate state on disk | None needed — restart pipeline | All data unchanged on disk | Nothing |

#### Stage 8: UpdateMetadataSections

**Operation:** Update per-image metadata section offsets.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| During update | In-memory operation; no disk changes until AtomicCommit | No intermediate state on disk | None needed — restart pipeline | All data unchanged on disk | Nothing |

#### Stage 9: AtomicCommit

**Operation:** Commit all index changes via dual-header protocol.

| Crash Point | Intermediate File State | Detection Method | Recovery Action | Data Preserved | Data Discarded |
|-------------|------------------------|------------------|-----------------|----------------|----------------|
| Before SecondaryHeader write | Shards may be compacted but index still has old offsets | Dual-header: both headers valid with old state | Use old headers; shards have compacted layout but old offsets still work (data at old offsets was preserved or shard was replaced atomically) | All data accessible via old index | Compact progress (will be re-done on next compact) |
| After SecondaryHeader, before PrimaryHeader | SecondaryHeader has new offsets; PrimaryHeader has old | Dual-header protocol | Use SecondaryHeader — new offsets are authoritative | All data with updated offsets | Nothing |
| After PrimaryHeader (complete) | Both headers have new offsets | No recovery needed | None | All data with updated offsets | Nothing |

---

## Summary Table

### AddImage (ImageWriter)

| Step | Operation | Crash Outcome | Recovery Action |
|------|-----------|---------------|-----------------|
| 1 | Append blocks to shard | Orphaned bytes beyond committed offset | Truncate shard to committed offset |
| 2 | Append metadata to shard | Orphaned bytes beyond committed offset | Truncate shard to committed offset |
| 3a | AtomicCommit: before secondary header | Orphaned shard data, old headers valid | Truncate shard to committed offset |
| 3b | AtomicCommit: after secondary, before primary | Secondary has new state | Use SecondaryHeader (image is committed) |
| 3c | AtomicCommit: after primary | Fully committed | None |
| 4 | Release writer lock | Stale lock file | Release stale lock on next open |

### DeleteImage

| Step | Operation | Crash Outcome | Recovery Action |
|------|-----------|---------------|-----------------|
| 1 | Append directory update | Unreferenced bytes in data region | Use last valid header (image stays live) |
| 2a | AtomicCommit: before secondary | Old headers valid | Use existing headers (image stays live) |
| 2b | AtomicCommit: after secondary, before primary | Secondary has delete | Use SecondaryHeader (image is removed) |
| 2c | AtomicCommit: after primary | Fully committed | None |

### RestoreImage

| Step | Operation | Crash Outcome | Recovery Action |
|------|-----------|---------------|-----------------|
| 1 | Append directory update | Unreferenced bytes in data region | Use last valid header (image stays removed) |
| 2a | AtomicCommit: before secondary | Old headers valid | Use existing headers (image stays removed) |
| 2b | AtomicCommit: after secondary, before primary | Secondary has restore | Use SecondaryHeader (image is live) |
| 2c | AtomicCommit: after primary | Fully committed | None |

### RollbackImage

| Step | Operation | Crash Outcome | Recovery Action |
|------|-----------|---------------|-----------------|
| 1 | Append directory update | Unreferenced bytes in data region | Use last valid header (all statuses unchanged) |
| 2a | AtomicCommit: before secondary | Old headers valid | Use existing headers (all statuses unchanged) |
| 2b | AtomicCommit: after secondary, before primary | Secondary has rollback | Use SecondaryHeader (rollback applied) |
| 2c | AtomicCommit: after primary | Fully committed | None |

### CompactSetSeparate

| Step | Operation | Crash Outcome | Recovery Action |
|------|-----------|---------------|-----------------|
| 1 | Write `.compact.tmp` | Partial `.compact.tmp` + original intact | Delete `.compact.tmp` |
| 2 | Validate `.compact.tmp` | Complete `.compact.tmp` + original intact | Delete `.compact.tmp` |
| 3 | Close old index | Complete `.compact.tmp` + original intact | Delete `.compact.tmp` |
| 4a | `File.Replace` (atomic) | At least one file survives | Use surviving file |
| 4b | Fallback: after delete, before move | Only `.compact.tmp` exists | Rename `.compact.tmp` → original, validate |
| 5 | Reopen index | Valid index at original path | Open normally |

### CompactSetEmbedded

| Step | Operation | Crash Outcome | Recovery Action |
|------|-----------|---------------|-----------------|
| 1a | Extract: write `.tmp` | `.tmp` + original embedded intact | Delete `.tmp` (TryRecover) |
| 1b | Extract: after first rename | Separated state | Continue as separate mode (TryRecover) |
| 1c | Extract: after both renames, before truncation | Oversized shard | Truncate shard to boundary |
| 2 | Open standalone index | Valid separated state | Open as separate mode |
| 3 | Compact shards | See CompactShards pipeline | See CompactShards recovery |
| 4 | Write `.compact.tmp` | Partial `.compact.tmp` + standalone intact | Delete `.compact.tmp` |
| 5 | Replace index (`File.Replace`) | At least one index survives | Use surviving file |
| 6a | Re-embed: index appended, no footer | Shard missing footer magic | Re-append footer (TryRecover) |
| 6b | Re-embed: footer written, before rename | Shard has valid footer | Complete rename (TryRecover) |
| 6c | Re-embed: rename complete | Fully embedded | None |

### CompactShards Pipeline

| Step | Operation | Crash Outcome | Recovery Action |
|------|-----------|---------------|-----------------|
| 1 | GatherReferencedKeys | No disk changes | None (restart pipeline) |
| 2 | BuildChunkList | No disk changes | None (restart pipeline) |
| 3 | GroupChunksByShard | No disk changes | None (restart pipeline) |
| 4a | CompactShard: write `.tmp` | Shard `.tmp` + original intact | Delete shard `.tmp` |
| 4b | CompactShard: `File.Replace` | At least one shard survives | Use surviving shard |
| 5 | DeleteEmptyShards | Some empty shards deleted | No action (they were empty) |
| 6 | UpdateBlockIndex | No disk changes (in-memory) | None (restart pipeline) |
| 7 | UpdateBlockMaps | No disk changes (in-memory) | None (restart pipeline) |
| 8 | UpdateMetadataSections | No disk changes (in-memory) | None (restart pipeline) |
| 9a | AtomicCommit: before secondary | Old headers valid | Use old headers (re-compact later) |
| 9b | AtomicCommit: after secondary, before primary | Secondary has new offsets | Use SecondaryHeader |
| 9c | AtomicCommit: after primary | Fully committed | None |

---

## Unrecoverable States

The following states cannot be automatically recovered and require manual intervention (restore from backup):

| Condition | Why It's Unrecoverable |
|-----------|----------------------|
| Both PrimaryHeader and SecondaryHeader are corrupt | No valid index state exists to recover from |
| Only `.compact.tmp` exists and fails header validation | The only available index file is corrupt; original was already deleted |
| No index file and no `.compact.tmp` exist | No index data exists on disk at all |

When an unrecoverable state is detected:
- `CheckSetHealth` returns `SetHealthStatus.Unrecoverable` with a description of what is missing
- The set cannot be opened
- Manual restoration from backup is required

---

## Maintenance

This document must be kept in sync with the implementation. When recovery logic changes:

1. Update the relevant failure scenario tables
2. Update the summary table
3. Verify the recovery state detection matrix is still accurate
4. Run the crash-recovery property tests to confirm all scenarios are covered

The crash-recovery property tests (`CrashRecoveryPropertyTests.cs`) serve as the executable specification for this document — if a scenario described here is not covered by a test, it should be added.
