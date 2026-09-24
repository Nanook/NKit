# NKit DataStore Format Specification

This document describes the binary storage format used by NKit DataStore — how data is structured on disk, how operations modify that structure, and how crash safety is maintained. It bridges the [README](../README.md)'s conceptual overview and the low-level technical references linked at the end.

## Overview

NKit DataStore uses a custom binary format to store content-addressed block data with deduplication. It works like an archiver — images go in, are deduplicated and compressed, and can be extracted or mounted on demand. The format scales from small per-game collections (embedded mode) to multi-TiB preservation libraries (separate mode with sharded storage).

Each **set** consists of:

- A **binary index file** (`.nkds`) containing headers, image metadata, block maps, a block index, and an image directory
- One or more **shard files** (`_NNNN.nkds`) containing compressed block payloads

The index file is the control plane — it knows where everything is. The shard files are the data plane — they hold the actual bytes.

Two storage modes exist:

| Mode | Condition | Layout |
|------|-----------|--------|
| **Separate** | `shardSize > 0` | Index and shards are separate files |
| **Embedded** | `shardSize = 0` | Everything in a single file (shard data + index + footer) |

## File Layout

### Compacted State

After compaction, the index file has a clean sequential layout:

```
┌─────────────────────────────────────────────────────────────┐
│ Primary Header (256 bytes)                                  │ offset 0x000
├─────────────────────────────────────────────────────────────┤
│ Secondary Header (256 bytes)                                │ offset 0x100
├─────────────────────────────────────────────────────────────┤
│ Directory Region (reserved space, default 4096 bytes)       │ offset 0x200
├─────────────────────────────────────────────────────────────┤
│ Image 1 — Metadata Section (zstd compressed)                │
│ Image 1 — BlockMap Section (zstd compressed)                │
├─────────────────────────────────────────────────────────────┤
│ Image 2 — Metadata Section (zstd compressed)                │
│ Image 2 — BlockMap Section (zstd compressed)                │
├─────────────────────────────────────────────────────────────┤
│ ...                                                         │
├─────────────────────────────────────────────────────────────┤
│ Block Index (sectioned zstd compression)                    │
├─────────────────────────────────────────────────────────────┤
│ Image Directory (zstd compressed, in directory region or    │
│   appended at end if region overflows)                      │
└─────────────────────────────────────────────────────────────┘
```

### Append State (Non-Compacted)

After add/remove operations, new data is appended beyond the original end. Superseded structures remain as dead space until compaction reclaims them:

```
┌─────────────────────────────────────────────────────────────┐
│ Primary Header (256 bytes)                                  │ ← Points to latest directory
├─────────────────────────────────────────────────────────────┤
│ Secondary Header (256 bytes)                                │ ← Updated before primary
├─────────────────────────────────────────────────────────────┤
│ Directory Region                                            │
├─────────────────────────────────────────────────────────────┤
│ [Original image sections...]                                │
│ [Original Block Index]                                      │
│ [Superseded Image Directory v1 — dead space]                │
├─────────────────────────────────────────────────────────────┤
│ ─── Append 1 ───                                            │
│ Image N — Metadata Section                                  │
│ Image N — BlockMap Section                                  │
│ Block Index Delta 1 (new blocks only)                       │
│ [Superseded Image Directory v2 — dead space]                │
├─────────────────────────────────────────────────────────────┤
│ ─── Append 2 ───                                            │
│ Image N+1 — Metadata Section                                │
│ Image N+1 — BlockMap Section                                │
│ Block Index Delta 2 (new blocks only)                       │
│ Image Directory v3 (current)                                │ ← Header points here
└─────────────────────────────────────────────────────────────┘
```

The header always points to the current (latest) image directory. Superseded directories and deltas accumulate as dead space, reclaimed by compaction.

## Header Structure

The file maintains two identical-format headers for crash safety (the dual-header atomic commit protocol). Each header is 256 bytes.

| Offset | Size | Type | Field | Description |
|--------|------|------|-------|-------------|
| `0x00` | 4 | uint32 | Magic | `0x4E4B4453` (ASCII "NKDS") |
| `0x04` | 2 | uint16 | MajorVersion | Format major version (currently `1`) |
| `0x06` | 2 | uint16 | MinorVersion | Format minor version (currently `0`) |
| `0x08` | 8 | int64 | ImageDirectoryOffset | File offset to current Image Directory |
| `0x10` | 4 | int32 | ImageDirectorySize | Compressed size of Image Directory in bytes |
| `0x14` | 4 | int32 | ImageDirectoryUncompressedSize | Uncompressed size (0 = stored uncompressed) |
| `0x18` | 8 | int64 | BlockIndexOffset | File offset to main Block Index |
| `0x20` | 8 | int64 | BlockIndexSize | Size of main Block Index in bytes |
| `0x28` | 4 | int32 | BlockIndexDeltaCount | Number of Block Index Delta entries |
| `0x2C` | 8 | int64 | BlockIndexDeltaHeadOffset | File offset to first delta (0 = no deltas) |
| `0x34` | 8 | int64 | FileEndOffset | Logical end of valid data |
| `0x3C` | 8 | int64 | ShardSize | Shard rotation size in bytes (0 = embedded mode) |
| `0x44` | 4 | int32 | BlockSize | Block size in bytes (default 65536) |
| `0x48` | 4 | int32 | MaxOffsetBlocks | Max block keys per offset record |
| `0x4C` | 4 | int32 | ImageCount | Total image count (including removed) |
| `0x50` | 4 | int32 | DirectoryRegionCapacity | Reserved directory region size in bytes |
| `0x54` | 8 | uint64 | HeaderChecksum | XXHash64 of bytes `0x00`–`0x53` |
| `0x5C` | 164 | — | _reserved_ | Zero-filled, reserved for future use |

**Primary Header** is at offset `0x000`. **Secondary Header** is at offset `0x100`.

### Validation Rules

A header is valid when:

1. Magic equals `0x4E4B4453`
2. MajorVersion equals the reader's expected major version
3. MinorVersion ≤ reader's supported minor version
4. `ImageDirectoryOffset` ≥ 512 (past both headers)
5. `BlockIndexOffset` ≥ 512
6. `FileEndOffset` ≥ 512
7. All size fields ≥ 0
8. `BlockSize` > 0
9. `MaxOffsetBlocks` > 0
10. `ImageCount` ≥ 0
11. `ShardSize` ≥ 0
12. `DirectoryRegionCapacity` ≥ 0

### Version Compatibility

A reader opens files where `MajorVersion` matches its expected major version and `MinorVersion` ≤ its supported minor version. Mismatches raise an exception.

## Image Sections

Each image stored in a set has two sections in the index file. They are stored as separate compressed payloads to enable independent access patterns:

### Two-Part Design Rationale

- **Metadata Section** — Lightweight information for VFS directory listings (area definitions, file records). Decompressed for directory browsing without touching block data.
- **BlockMap Section** — Heavier offset/block mapping data needed only when serving actual file content. Only decompressed on data reads.

This separation means mounting a set and browsing its filesystem only decompresses the small metadata sections (~1–5 KiB each), while the larger block map sections (~50–500 KiB each) remain compressed until content is actually read.

### Image Metadata Section

Stored as a zstd-compressed payload (level 19). Contains:

- **Structure version** (uint16)
- **Area records** — Define logical regions within the image (offset, size, stride parameters, checksums, metadata blobs)
- **File records** — Auxiliary files stored alongside block data in shards (name, shard location, sizes)

Areas describe the content-aware structure of the image (e.g., Wii disc partitions, GC filesystem regions). Each area carries stride parameters that define how blocks map to logical offsets within that area.

### Image BlockMap Section

Stored as a zstd-compressed payload (level 19). Contains:

- **Structure version** (uint16)
- **Block locations** — Unique block entries with their shard file positions (XXHash64 + CRC32 key, fileId, offset, compressed size)
- **Offset records** — Logical offset mappings that reference block locations by index (offset, size, block type, block indices)

The offset records define how to reconstruct any byte range of the image by looking up the appropriate blocks from shard files.

## Block Index

The Block Index is a sorted array of all known blocks across the entire set. It serves exclusively the write path — enabling O(log n) binary search for deduplication during image addition. Readers never load the block index.

### Main Block Index

Stored with sectioned zstd compression for parallel loading:

```
[StructureVersion: uint16]
[SectionCount: uint32]
[Section 0: CompressedSize(uint32) + UncompressedSize(uint32) + ZstdData]
[Section 1: ...]
...
[Section N-1: ...]
```

Each section defaults to 65,536 bytes uncompressed (2,340 entries at 28 bytes each). Sections are independently decompressible, enabling parallel loading on multi-core systems.

### Block Index Entry (28 bytes, sorted by key)

| Field | Size | Description |
|-------|------|-------------|
| XxHash64 | 8 | Block content hash (part 1 of composite key) |
| Crc32 | 4 | Block content CRC (part 2 of composite key) |
| FileId | 4 | Shard file containing this block |
| Offset | 8 | Byte offset within the shard file |
| Size | 4 | Compressed size in the shard |

Entries are sorted ascending by `(XxHash64, Crc32)` for binary search.

### Block Index Deltas

Each write operation that introduces new blocks appends a delta containing only those new blocks. Deltas form a linked list:

```
[StructureVersion: uint16]
[EntryCount: uint32]
[NextDeltaOffset: int64]       ← File offset of previous delta (0 = end of chain)
[CompressedSize: uint32]
[UncompressedSize: uint32]
[ZstdCompressedEntries]        ← Same 32-byte entry format, sorted
```

The header's `BlockIndexDeltaHeadOffset` points to the most recent delta. Each delta's `NextDeltaOffset` points to the next older delta.

During writes, the main Block Index plus all deltas are merged into a single sorted in-memory array. During compaction, all deltas are merged into the main Block Index and the delta chain is eliminated.

## Image Directory

The Image Directory is a compact lookup table loaded into memory when the file is opened. It provides O(1) access to any image's metadata and section locations.

### Wire Format

```
[StructureVersion: uint16]
[EntryCount: uint32]
[Entry 0]
[Entry 1]
...
[Entry N-1]
```

The directory is zstd-compressed when stored. It may reside in the reserved directory region (offset `0x200`, default 4096 bytes capacity) or be appended at the file end if it outgrows the reserved space.

### Directory Entry Fields

Each entry contains:

- **ImageId** — Unique identifier
- **Name** — UTF-8 encoded image name
- **Size** — Image size in bytes
- **Checksums** — CRC32 and XXHash64 of the full image
- **System** — Optional system identifier string (e.g., "Wii", "GC")
- **Format** — Image format enum value
- **Rollback info** — Shard file ID and offset for rollback data (-1 if none)
- **Removed flag** — Soft-delete marker (1 = removed, 0 = active)
- **Section pointers** — File offsets and compressed sizes for both the metadata and block map sections

The directory is replaced atomically via reference swap when updated — existing readers holding the old reference continue safely.

## Shard File Structure

Shard files (`{set}_NNNN.nkds`) store compressed block payloads and auxiliary file data. They are the data plane of the storage system.

### Layout

Shard files are simple append-only containers:

```
┌─────────────────────────────────────────────────────────────┐
│ Block 0 (zstd compressed payload)                           │
│ Block 1 (zstd compressed payload)                           │
│ ...                                                         │
│ File Record data (auxiliary files)                          │
│ Block N (zstd compressed payload)                           │
│ ...                                                         │
└─────────────────────────────────────────────────────────────┘
```

Blocks and file records are interleaved in write order. Each block's location is tracked by the block index (fileId + offset + size). Each file record's location is tracked by the image metadata section.

### Shard Rotation

When `ShardSize > 0`, a new shard file is created when the current one exceeds the configured size limit. Shard files are numbered sequentially (`_0000`, `_0001`, etc.). This bounds individual file sizes for filesystem compatibility and backup efficiency.

### Block Addressing

To read a block:
1. Look up the block key `(XXHash64, CRC32)` in the image's block map section to get `(fileId, offset, size)`
2. Open shard file `{set}_{fileId:D4}.nkds`
3. Seek to `offset`, read `size` bytes
4. Decompress the zstd payload

## Embedded Mode

When `ShardSize = 0`, everything lives in a single file. This mode is best suited for smaller sets (e.g., per-game 1GMR collections) where single-file simplicity is preferred. For large-scale archiving, separate mode with configurable shard sizes is recommended.

Block data occupies the front of the file, and the binary index is appended at the end with a 12-byte footer:

```
┌─────────────────────────────────────────────────────────────┐
│ Block Data (shard region)       [0, ShardBoundary)          │
├─────────────────────────────────────────────────────────────┤
│ Binary Index                    [ShardBoundary, EOF-12)     │
│   (same layout as separate mode: headers, directory region, │
│    image sections, block index, image directory)            │
├─────────────────────────────────────────────────────────────┤
│ Embedded Footer (12 bytes)      [EOF-12, EOF)               │
│   IndexSize: int64 BE (8 bytes)                             │
│   Magic: uint32 BE (4 bytes) = 0x4E4B4453 ("NKDS")          │
└─────────────────────────────────────────────────────────────┘
```

The **Shard Boundary** = `file_size - 12 - IndexSize` — where block data ends and the index begins.

### Embedded Mode Considerations

- The index region uses a `baseOffset` equal to the shard boundary, so all internal offsets are relative to the start of the index region
- When images are removed, the index grows (new directory appended) but the footer's `IndexSize` is **not** updated — it becomes stale
- The header's `FileEndOffset` is the authoritative index size, not the footer
- Compaction temporarily extracts to separate mode, compacts, then re-embeds (see [Operations](#operations))

## Operations

### Add Image

1. Blocks are compressed and appended to the current shard file
2. For each block, the in-memory block index is checked for deduplication — existing blocks are skipped
3. Image metadata section and block map section are serialized and appended to the index file
4. A Block Index Delta is appended (containing only newly-written blocks)
5. An updated Image Directory is written
6. AtomicCommit makes everything visible

**Structural effect:** Index file grows by the size of two image sections + one delta + one directory. Shard file grows by the size of new (non-deduplicated) blocks.

### Read Image

1. Image Directory (in memory) provides section offsets
2. Metadata section is decompressed for area/file listings
3. Block map section is decompressed for content reads
4. Blocks are read from shard files by (fileId, offset, size)

**Structural effect:** None — reads are non-mutating.

### Remove Image

1. The image's directory entry is updated with `Removed = 1`
2. An updated Image Directory is appended
3. AtomicCommit makes the removal visible

**Structural effect:** Index file grows by one directory append. Block data and image sections remain on disk (reclaimed by compaction).

### Restore Image

1. The image's directory entry is updated with `Removed = 0`
2. An updated Image Directory is appended
3. AtomicCommit makes the restoration visible

**Structural effect:** Same as Remove — one directory append.

### Rollback

1. All images' removed/live statuses are reverted to their state at a specified point
2. An updated Image Directory is appended
3. AtomicCommit makes the rollback visible

**Structural effect:** Same as Remove/Restore — one directory append.

### Compact

Compaction reclaims all dead space accumulated from appends and removals:

1. **Shard compaction** — Orphaned blocks (referenced only by removed images) are identified and removed from shard files using copy-on-write (write to `.tmp`, then atomic replace)
2. **Index compaction** — The index file is rewritten from scratch:
   - Removed images' sections are excluded
   - All Block Index Deltas are merged into a single main Block Index
   - Block index entries not referenced by any remaining image are pruned
   - All structures are re-serialized at the latest version
   - The result is a clean, defragmented sequential layout

For embedded mode, compaction temporarily extracts to separate mode (index and shard as separate files), performs the compaction, then re-embeds the result.

**Structural effect:** File sizes decrease. Dead space is eliminated. The file returns to the compacted layout.

## Crash Safety

NKit DataStore is designed to survive crashes at any point during any operation without data loss or corruption.

### Dual-Header Protocol

The core crash safety mechanism. During AtomicCommit:

1. All appended data is flushed to disk
2. Secondary Header (offset `0x100`) is written with the new state → flushed
3. Primary Header (offset `0x000`) is written with the new state → flushed

**Recovery logic:**

| Primary Header | Secondary Header | Action |
|----------------|------------------|--------|
| Valid | Valid (agrees) | Normal — use Primary |
| Valid | Valid (disagrees) | Use Primary (written last, authoritative) |
| Invalid/corrupt | Valid | Use Secondary — crash between steps 2 and 3 |
| Invalid/corrupt | Invalid/corrupt | **Unrecoverable** — file is corrupt |

This guarantees that at least one valid header exists after any single crash during commit, because the headers are written sequentially with flushes between them.

### Append-Only Semantics

All writes append new data at the end of the file. Previously written bytes are never modified (except the two header regions during atomic commit). This means:

- A crash before commit leaves the file at its previous valid state
- Superseded directories and deltas remain as dead space (harmless, reclaimed by compaction)
- No write amplification from in-place updates

### Copy-on-Write for Shard Compaction

When shard files are rewritten during compaction:

1. Live data is written to a `.tmp` file
2. The `.tmp` file is flushed to disk
3. The original shard is atomically replaced (`File.Replace` or delete + rename)

If a crash occurs before step 3, the original shard remains intact and the `.tmp` is cleaned up on recovery.

### Recovery States

On open, the system detects and recovers from intermediate states:

| State | Detection | Recovery |
|-------|-----------|----------|
| Orphaned shard tail | Shard file larger than committed block index offset | Truncate to committed offset |
| Stale `.compact.tmp` | Temp file exists alongside original | Delete temp file |
| Missing original (compact) | Only `.compact.tmp` exists | Rename to original, validate |
| Shard `.tmp` files | Temp shard alongside original | Delete temp shard |
| Embedded mid-extraction | `.tmp` + original embedded file | Delete `.tmp`, use original |
| Separated state (embedded) | Index + shard as separate files, no footer magic | Continue as separate mode |
| Stale writer lock | Lock file with no active process | Release lock |

Recovery always restores the set to the state of the last successful AtomicCommit. No partially-applied changes are ever visible after recovery.

### Per-Operation Safety Summary

| Operation | Strategy |
|-----------|----------|
| Add Image | Append-only + dual-header commit |
| Remove/Restore/Rollback | Append-only + dual-header commit |
| Shard compaction | Copy-on-write (`.tmp` → atomic replace) |
| Index compaction | Write to `.compact.tmp` → validate → atomic replace |
| Embedded extraction | Rename-based state machine |
| Re-embed | Append index + footer → rename |

## Auxiliary Stores

An **aux store** is a companion set that provides fallback block data for images with missing blocks. When a block is not found in the primary set's shards, the system checks the aux store before reporting an error.

This enables scenarios where a set contains metadata and block maps for images whose actual block data lives in a shared pool (the aux store), avoiding duplication across multiple sets that share common content.

Aux stores use the same binary format — they are regular sets that happen to be referenced as block providers by other sets.

## NkFs Filesystem Format

NKit DataStore uses the **NkFs** binary format to represent filesystem trees compactly within image metadata. NkFs replaces variable-length text formats with a fixed-size binary structure inspired by the GameCube/Wii FST (File System Table).

Key characteristics:

- **12-byte fixed entries** — Constant-time O(1) index-based lookup
- **Flat navigation** — Directory listing and path resolution operate directly on the entry table using parent/next-entry indices (no in-memory tree built)
- **Big-endian encoding** — Matches GameCube/Wii FST convention
- **String table with deduplication** — Directory names are deduplicated; file entries carry variable-width prefix bytes encoding size, checksums, and image index

The format consists of a 12-byte header (magic `0x4E4B4653` "NKFS", version, entry count), followed by the entry table (N × 12 bytes), followed by a variable-length string table.

See [NkFsFormat.md](../NkFsFormat.md) for the complete binary specification.

## Checksums and Compression

### Checksums

- **Header integrity** — XXHash64 of bytes `0x00`–`0x53` stored at offset `0x54`
- **Block identification** — Each block is identified by a composite key of `(XXHash64, CRC32)` computed over the uncompressed block data
- **Image integrity** — Each image carries a CRC32 and XXHash64 of its full uncompressed content

### Compression

- **Algorithm** — Zstd (Zstandard) throughout
- **Index data** — Level 19 for metadata sections, block map sections, block index sections, and image directory
- **Block payloads** — Compressed before writing to shard files (level may vary)
- **Sectioned compression** — The Block Index uses independently-decompressible sections (default 64 KiB uncompressed each) for parallel loading

### Per-Structure Versioning

Every serialized structure begins with a `uint16` structure version, allowing each to evolve independently:

| Structure | Current Version |
|-----------|----------------|
| File Header | Major 1, Minor 0 |
| Image Metadata Section | 1 |
| Image BlockMap Section | 1 |
| Block Index | 1 |
| Block Index Delta | 1 |
| Image Directory | 1 |

Compaction uplifts all structures to the latest version.

## Further Reading

- [COMPACTION.md](COMPACTION.md) — Detailed compaction algorithm and embedded mode lifecycle
- [RECOVERY.md](RECOVERY.md) — Complete failure scenario catalog and recovery state machine
- [NkFsFormat.md](NkFsFormat.md) — NkFs binary filesystem format specification
