# Compaction

This document describes the `CompactSet` operation in NKitDataStore — how it reclaims space after image removal, the differences between separate and embedded mode compaction, aux/file data handling, crash recovery, and the correctness guarantees enforced by the implementation.

## Overview

When images are removed from a set, their directory entries are marked as removed but their block data and metadata sections remain on disk. `CompactSet` reclaims this space by:

1. Removing orphaned blocks from shard files (blocks only referenced by removed images)
2. Rewriting the index file without removed images' directory entries, metadata sections, and block map sections
3. Pruning the block index to contain only entries referenced by live images

The operation is designed to be crash-safe at every step — an interrupted compact can be recovered on the next open.

## Storage Modes

### Separate Mode (shardSize > 0)

The index and block data live in separate files:

```
test.nkds          ← Binary index (headers, directory, block index, image sections)
test_0000.nkds     ← Shard file 0 (block payloads + file data)
test_0001.nkds     ← Shard file 1 (overflow when shard 0 exceeds shardSize)
```

### Embedded Mode (shardSize = 0)

Everything lives in a single file with block data at the front and the index appended at the end:

```
┌─────────────────────────────────────────────────────────┐
│ Block Data (shard region)  [0, Shard_Boundary)          │
├─────────────────────────────────────────────────────────┤
│ Binary Index               [Shard_Boundary, EOF-12)     │
├─────────────────────────────────────────────────────────┤
│ Embedded Footer (12 bytes) [EOF-12, EOF)                │
│   IndexSize: int64 BE (8 bytes)                         │
│   Magic: "NKDS" uint32 BE (4 bytes) = 0x4E4B4453       │
└─────────────────────────────────────────────────────────┘
```

The **Shard Boundary** = `file_size - FooterSize - IndexSize` = where block data ends and the index begins.

## Compaction Flow

### Entry Point: `CompactSet`

```
CompactSet(setName)
  ├── if embedded mode → CompactSetEmbedded
  └── if separate mode → CompactSetSeparate
```

### Separate Mode Flow

```
CompactSetSeparate(setName)
  │
  ├── Step 1: CompactShards(setName, indexFile)
  │     └── Removes orphaned blocks/files from shard files
  │         Updates block index, block maps, and metadata in the live index
  │
  ├── Step 2: indexFile.CompactTo(tempPath)
  │     └── Rewrites the index file without removed images
  │         Produces a clean, defragmented index
  │
  ├── Step 2b: Validate compacted output
  │     └── Opens temp file to verify header passes validation
  │         If corrupt → delete temp, preserve original, throw
  │
  ├── Step 3: Dispose old index file handle
  │
  ├── Step 4: Replace original with compacted temp
  │     └── File.Delete(original) → File.Move(temp, original)
  │
  └── Step 5: Reopen compacted file, update caches
```

### Embedded Mode Flow

```
CompactSetEmbedded(setName)
  │
  ├── Check: Already in separated mode? (crash recovery)
  │     └── If test.nkds + test_0000.nkds exist and test.nkds has no footer magic
  │         → Skip extraction, use existing separated files
  │
  ├── Step 1: Extract to separate mode
  │     └── Uses Header.FileEndOffset as true index size (not stale footer)
  │         EmbeddedIndexCommitter.ExtractToSeparateMode(shardBoundary, actualIndexSize)
  │
  ├── Step 2: Open standalone index (baseOffset=0)
  │
  ├── Step 2b: CompactShards(setName, standaloneIndex)
  │     └── Same shard compaction as separate mode
  │
  ├── Step 3: standaloneIndex.CompactTo(compactTempPath)
  │
  ├── Step 3b: Validate compacted output
  │
  ├── Step 4: Replace standalone index with compacted version
  │
  ├── Step 5: ReEmbed(indexPath, shardPath)
  │     └── Appends index content + EmbeddedFooter to shard file
  │         Renames shard → embedded file path
  │
  └── Step 6: Reopen as embedded, compute new boundary, update caches
```

## CompactShards — Shard File Compaction

This is the core operation that reclaims space from shard files. It operates on the block data layer.

### Algorithm

```
CompactShards(setName, indexFile)
  │
  ├── Early exit: no images at all (empty set, never had images) → return
  │
  ├── Empty set handling: all images removed
  │     └── Delete all shard files, clear block index, AtomicCommit, return
  │
  ├── 1. Gather referenced block keys from all live images
  │     └── For each live image: read its BlockMap section → collect BlockKeys
  │
  ├── 2. Build chunk list (blocks + file records)
  │     └── Classify each block index entry as referenced or unreferenced
  │         Gather file records from live images' metadata sections
  │
  ├── 3. Group chunks by shard, compact each one
  │     └── For each shard with live data:
  │           Compute new contiguous offsets
  │           If gaps exist (requiresCompaction=true):
  │             Copy-on-Write: write live data to .tmp → flush → File.Replace
  │             Record offset changes
  │
  ├── 4. Delete empty shard files
  │     └── Shards with no live data (all blocks unreferenced) → File.Delete
  │
  ├── Guard: if no unreferenced blocks AND no offset changes → return
  │
  ├── 5. Update block index
  │     └── Apply new offsets to moved blocks
  │         Remove unreferenced entries
  │         ReplaceBlockIndex(liveEntries)
  │
  ├── 6. Update block map sections for images with moved blocks
  │     └── For each live image with moved blocks:
  │           Read BlockMap → update offsets → UpdateImageBlockMap
  │
  ├── 7. Update metadata sections for images with moved file records
  │     └── For each live image with moved file records:
  │           Read metadata → update offsets → UpdateImageMetadata
  │
  └── 8. AtomicCommit()
        └── Persists all changes (block index, directory, sections) atomically
```

### Key Invariants

- **Block index pruning always executes** when there are unreferenced entries, regardless of whether any shard file was physically rewritten. This prevents orphaned block index entries from accumulating.
- **Shared blocks** (referenced by multiple live images) are never removed — only blocks exclusively referenced by removed images are pruned.
- **Sets with no removed images** produce no changes — no shard rewrites, no block index modifications.

## CompactTo — Index File Compaction

Rewrites the entire index file from scratch, producing a clean defragmented output.

### Layout of Compacted File

```
Offset 0x000: Primary Header (256 bytes)
Offset 0x100: Secondary Header (256 bytes)
Offset 0x200: Directory Region (compressed directory + padding)
              Image sections (metadata + block map per live image)
              Block Index (zstd-compressed, only live entries)
```

### Algorithm

1. Gather non-removed images from directory
2. Load and merge block index (main + all deltas)
3. Build set of live BlockKeys from all live images' block maps
4. Filter block index to only retain live entries
5. Estimate directory region capacity
6. Write placeholder headers + directory region
7. Copy each live image's metadata and block map sections (raw bytes)
8. Write pruned block index (zstd compressed)
9. Re-serialize directory with final offsets
10. Write final headers with correct pointers
11. Flush to disk

### What Gets Removed

- Directory entries for removed images
- Metadata sections for removed images
- Block map sections for removed images
- Block index entries not referenced by any live image
- Block index deltas (merged into a single clean index)

## Aux Data and File Records

Aux data (auxiliary file records stored alongside block data in shards) is handled during compaction:

- **File records** are tracked in each image's metadata section with `(ImageId, FileName, FileId, Offset, Size)`
- During `CompactShards`, file record chunks are included in the shard compaction alongside block chunks
- When a shard is rewritten, file record offsets are updated in the metadata sections (step 7)
- File records for removed images are not included in the chunk list (only live images' file records are gathered)

## Embedded Mode Specifics

### Stale Footer Problem

When images are removed via `DeleteImage`, the index grows (new directory appended at end, `FileEndOffset` updated in header). However, the `EmbeddedFooter.IndexSize` at the end of the file is NOT updated — it still reflects the size from the last re-embed operation.

**Solution**: `CompactSetEmbedded` uses `indexFile.Header.FileEndOffset` as the authoritative index size for extraction, not the footer's stale `IndexSize`.

### Extraction Process (EmbeddedIndexCommitter)

```
ExtractToSeparateMode(shardBoundary, knownIndexSize)
  │
  ├── 1. Copy index bytes [shardBoundary, shardBoundary+knownIndexSize) → test.nkds.tmp
  │     └── Flush to disk
  │
  ├── 2. Rename test.nkds → test_0000.nkds (embedded file becomes shard)
  │
  ├── 3. Rename test.nkds.tmp → test.nkds (tmp becomes standalone index)
  │
  └── 4. Truncate test_0000.nkds to shardBoundary (remove old index+footer from shard)
```

### Re-Embed Process

```
ReEmbed(indexPath, shardPath)
  │
  ├── 1. Read entire index file content
  │
  ├── 2. Append index content + EmbeddedFooter to shard file
  │     └── Footer = [indexContent.Length as int64 BE] + [0x4E4B4453]
  │         Flush to disk
  │
  ├── 3. Delete old index file (test.nkds)
  │
  └── 4. Rename shard (test_0000.nkds) → test.nkds (now the final embedded file)
```

## Crash Safety

### Defensive Write Practices

| Operation | Strategy |
|-----------|----------|
| Shard file rewrite | Write to `.tmp` → `Flush(flushToDisk: true)` → `File.Replace` (atomic on Windows) |
| Index compaction | Write to `.compact.tmp` → validate → `File.Delete` + `File.Move` |
| Block index update | Append-only writes → dual-header atomic commit |
| Embedded extraction | Rename-based state machine (never in-place truncate of main file) |

### AtomicCommit Protocol

The `BinaryIndexFile.AtomicCommit()` uses a dual-header protocol:

1. Flush all appended data to disk
2. Write SecondaryHeader (at offset 0x100) → flush
3. Write PrimaryHeader (at offset 0x000) → flush

On recovery: if PrimaryHeader is corrupt but SecondaryHeader is valid, the secondary is authoritative. Both headers must agree for the file to be considered clean.

### Crash Recovery States

The `EmbeddedIndexCommitter.TryRecover()` handles these intermediate states:

| State | Files Present | Recovery Action |
|-------|--------------|-----------------|
| Normal embedded | `test.nkds` (with footer magic) | None needed |
| Mid-extraction | `test.nkds` + `test.nkds.tmp` | Delete `.tmp`, re-evaluate |
| Separated mode | `test.nkds` (no magic) + `test_0000.nkds` | Continue as separate mode |
| Mid-re-embed (complete) | `test_0000.nkds.tmp` (with footer magic) | Rename `.tmp` → `test.nkds` |
| Mid-re-embed (incomplete) | `test_0000.nkds.tmp` (no magic) + `test.nkds` | Re-append index + footer, rename |

### CompactSetEmbedded Recovery

Before extraction, checks if already in separated mode (from a previous failed compact):

```csharp
bool alreadySeparated = File.Exists(setBasePath) 
    && File.Exists(expectedShardPath) 
    && !DetectEmbeddedMode(setBasePath);
```

If true, skips extraction and proceeds directly with compaction of the existing separated files.

### CompactSetSeparate Recovery

On failure: cleans up `.compact.tmp` file and attempts to reopen the original index file so the set remains usable.

## Validation

After `CompactTo` writes the temp file, a validation step opens it with `BinaryIndexFile.Open()` to verify the header passes structural validation before replacing the original. If validation fails, the temp file is deleted and the original is preserved.

Header validation checks:
- Magic bytes match
- Version is supported
- All file-offset pointers are non-negative and within the index region
- Configuration values (BlockSize, MaxOffsetBlocks, ShardSize, ImageCount) are sane

## Correctness Properties

The following properties are enforced by the implementation and verified by property-based tests:

1. **Block Index Pruning**: After `CompactSet`, the block index contains ONLY entries referenced by at least one live image, regardless of whether shard files required physical rewriting.

2. **Empty Set Cleanup**: When all images are removed and `CompactSet` is called, the block index is empty and no shard files remain on disk.

3. **Incremental Integrity**: After any sequence of removals with `CompactSet` after each, every intermediate state is valid — all remaining images pass full verification.

4. **No-Removal Preservation**: Compacting a set with no removed images produces no changes (shard files byte-for-byte identical, block index unchanged).

5. **Shared Block Preservation**: Blocks referenced by multiple live images are never removed during compaction.

6. **Crash Safety**: An interrupted compact at any point leaves the set in a recoverable state.

## Performance Characteristics

- **Shard compaction** is O(live data size) — only live chunks are copied
- **Index compaction** is O(live images × section size) — copies all live sections sequentially
- **Block index pruning** is O(total block entries) — single pass with HashSet lookup
- **Copy-on-write** means the original shard is intact until the atomic replace succeeds
- **Shards that don't need compaction** (all live data already contiguous) are skipped entirely

## File Naming Conventions

| File | Purpose |
|------|---------|
| `{set}.nkds` | Main index file (separate mode) or embedded file |
| `{set}_NNNN.nkds` | Shard files (block data, numbered 0000, 0001, ...) |
| `{set}.nkds.compact.tmp` | Temporary compacted index (separate mode) |
| `{set}.nkds.tmp` | Temporary extracted index (embedded mode extraction) |
| `{set}_0000.nkds.tmp` | Temporary shard during re-embed |
| `{set}_NNNN.nkds.tmp` | Temporary shard during copy-on-write compaction |
