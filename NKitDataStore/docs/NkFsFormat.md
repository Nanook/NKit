# NkFs Binary Filesystem Format

## Overview

NkFs is a compact binary representation of filesystem trees for the NKit DataStore. It replaces the variable-length YAML text format (`filesystem.yaml` via `FsYaml`) with a fixed-size binary format inspired by the GameCube/Wii FST (File System Table), extended for 64-bit addressing.

Key characteristics:

- **Constant-time entry access**: 12-byte fixed entries allow O(1) index-based lookup.
- **Flat navigation**: Directory listing and path resolution operate directly on the flat entry table using parent/next-entry indices — no in-memory tree is built.
- **Compact representation**: Binary encoding with directory name deduplication and variable-width string table prefixes produces smaller output than UTF-8 YAML for non-trivial trees (100+ entries).
- **Round-trip fidelity**: Full bidirectional conversion with `FsYaml` ensures backward compatibility.

All multi-byte integers are stored **big-endian**, matching the GameCube/Wii FST convention and the existing `HashBlob` encoding in the project.

## Binary Layout

The binary format is a single contiguous byte sequence with three regions:

```
Offset 0x00
┌──────────────────────────────────────────────────────────┐
│  Header (12 bytes)                                       │
│    Magic, Version, Reserved, EntryCount                  │
├──────────────────────────────────────────────────────────┤  ← Offset 0x0C
│  Entry Table (N × 12 bytes)                              │
│    Entry 0: Root directory                               │
│    Entry 1..N-1: Files and directories in depth-first    │
│                  pre-order                               │
├──────────────────────────────────────────────────────────┤  ← Offset 12 + N×12
│  String Table (variable length, 2-byte aligned entries)  │
│    Directory entries: [name\0] + optional pad byte       │
│    File entries: [prefix byte][variable fields][name\0]  │
│                  + optional pad byte                     │
│    Directory names may share offsets (deduplication)     │
└──────────────────────────────────────────────────────────┘
```

The string table offset is computed: `12 + EntryCount × 12` (no field in header).

## Header Format (12 bytes)

| Offset | Size (bytes) | Field | Type | Description |
|--------|-------------|-------|------|-------------|
| 0x00 | 4 | Magic | uint32 | `0x4E4B4653` — ASCII "NKFS". Identifies the format. |
| 0x04 | 2 | Version | uint16 | Format version. Currently `1`. |
| 0x06 | 2 | Reserved | uint16 | Reserved for future use. Must be `0`. |
| 0x08 | 4 | EntryCount | int32 | Total number of entries in the entry table, including the root entry at index 0. |

StringTableOffset is computed: `12 + EntryCount × 12`. No StringTableOffset field in the header.

### Validation on Deserialization

- If the data is shorter than 12 bytes, deserialization fails with a truncated-data error.
- If the magic number does not equal `0x4E4B4653`, deserialization fails with an invalid-magic error.
- If the version is not `1`, deserialization fails with an unsupported-version error.
- If the data is shorter than `12 + EntryCount × 12`, deserialization fails with a truncated-data error.

## Entry Format (12 bytes)

Each entry in the entry table occupies exactly 12 bytes. Entries are ordered in **depth-first pre-order** traversal of the filesystem tree.

| Offset | Size (bytes) | Field | Type | Description |
|--------|-------------|-------|------|-------------|
| 0x00 | 4 | FlagsAndNameOffset | uint32 | High 3 bits = Flags, low 29 bits = NameOffset (×2 for actual byte offset) |
| 0x04 | 8 | (varies) | — | **File**: image offset (int64). **Directory**: parent index (int32) + next-entry index (int32). |

### Flags (3 bits)

The flags occupy the high 3 bits of the first 4 bytes (bits 31–29):

| Bit | Mask | Name | Description |
|-----|------|------|-------------|
| 0 | `0x01` | is_directory | `1` = directory, `0` = file |
| 1 | `0x02` | system_flag | System marker — the `system_flag` flag is the sole indicator of system status |
| 2 | `0x04` | image_file | Entry is an image file system reference |

### NameOffset Encoding

The 29-bit NameOffset is multiplied by 2 to get the actual byte offset into the string table. This means all string table entries must be 2-byte aligned (pad with a null byte when needed). This gives 1 GiB of addressable string table space (2²⁹ × 2 = 2³⁰ bytes).

```
Encoding:  packed = (flags << 29) | (nameOffset & 0x1FFFFFFF)
Decoding:  flags  = packed >> 29
           nameOffset = packed & 0x1FFFFFFF
           actualByteOffset = nameOffset × 2
```

### File Entry Interpretation (0x04–0x0B)

| Field | Size | Meaning |
|-------|------|---------|
| ImageOffset | 8 bytes (int64) | Byte offset of the file's data in the source image |

File size, checksums, and image ID are stored in the string table prefix (see below).

### Directory Entry Interpretation (0x04–0x0B)

| Field | Size | Meaning |
|-------|------|---------|
| ParentIndex | 4 bytes (int32) | Entry index of the parent directory (root's parent is 0) |
| NextEntryIndex | 4 bytes (int32) | Index of the first entry that is NOT a descendant of this directory |

### Root Entry (Index 0)

The root entry is always at index 0. It is a directory with:
- `is_directory = 1`
- `ParentIndex = 0` (self-referencing)
- `NextEntryIndex = EntryCount` (spans the entire entry table)
- `NameOffset` points to an empty string (single null byte) in the string table

## String Table Format

The string table is a packed byte region following the entry table. All entries are **2-byte aligned** (padded with a null byte when needed).

### Directory String Table Entries

Directories have no prefix byte — just the null-terminated name:

```
[name\0] + optional pad byte for 2-byte alignment
```

Directory names are eligible for deduplication (see below).

### File String Table Entries

Files have a 1-byte prefix followed by variable-width fields:

```
[1 byte prefix flags]
  bit 0: has_checksums (1 = 12 bytes xxHash64+CRC32 present, 0 = absent)
  bits 1-2: size_width (0=0 bytes, 1=2 bytes, 2=4 bytes, 3=8 bytes)
  bits 3-4: imageid_width (0=0 bytes, 1=2 bytes, 2=4 bytes, 3=8 bytes)
  bit 5: has_more_extents (1 = next sibling with same name is a continuation extent)
  bits 6-7: reserved, must be written as 0
[size_width bytes: file size, big-endian]
[if has_checksums: 8 bytes xxHash64 + 4 bytes CRC32, big-endian]
[imageid_width bytes: image index, big-endian]
[name\0]
+ optional pad byte for 2-byte alignment
```

#### Prefix Byte Bit Layout

```
Bit:  7   6   5   4   3   2   1   0
     [res][res][HME][iw1][iw0][sw1][sw0][chk]

chk  (bit 0):    has_checksums — 1 = xxHash64+CRC32 present after size field
sw   (bits 1-2): size_width — 0/1/2/3 → 0/2/4/8 bytes
iw   (bits 3-4): imageid_width — 0/1/2/3 → 0/2/4/8 bytes
HME  (bit 5):   has_more_extents — 1 = this entry has a continuation extent
res  (bits 6-7): reserved, must be 0
```

### Width Encoding

```
EncodeWidth(value):
  if value == 0: return 0  → 0 bytes
  if value ≤ 0xFFFF: return 1  → 2 bytes
  if value ≤ 0xFFFFFFFF: return 2  → 4 bytes
  else: return 3  → 8 bytes
```

### System Node Names

When the `system_flag` flag (bit 1) is set, the entry is a system node. The flag alone indicates system status — names are stored plain (without any `/` prefix) in the string table.

## Directory Navigation

Navigation operates directly on the flat entry table without building an in-memory tree.

### Child Listing Algorithm

To list the immediate children of a directory at entry index `N`:

```
1. Let nextEntry = entry[N].NextEntryIndex
2. Set i = N + 1
3. While i < nextEntry:
   a. Yield entry[i] as a direct child
   b. If entry[i] is a directory:
        Set i = entry[i].NextEntryIndex   (skip entire subtree)
      Else:
        Set i = i + 1
```

### Path Resolution Algorithm

To resolve a path string like `"dir1/dir2/file.txt"` to an entry index:

```
1. If path is null or empty, return 0 (root index)
2. Split path by '/'
3. Filter out empty segments
4. Set current = 0 (root)
5. For each segment:
   a. Scan direct children of entry[current]
   b. Compare each child's name with the segment
   c. If match found, set current = that child's index
   d. If no match, return -1 (not found)
6. Return current
```

## Directory Name Deduplication

Directory entries in the string table have no prefix byte. When the builder encounters a directory name that is an exact case-sensitive match to a directory name already written to the string table, it reuses the existing `NameOffset` instead of appending a duplicate.

This optimization is **not** applied to file entries, because file entries have prefix bytes with variable-width fields that may differ between files sharing the same filename.

## Multi-Extent File Chaining

NkFs supports files whose data spans multiple non-contiguous disc regions (multi-extent or split files). This is represented using consecutive same-name file entries within a parent directory, linked by the `has_more_extents` flag (bit 5) in the prefix byte.

### Chaining Rule

When the `has_more_extents` flag (bit 5) is set to `1` on a file entry's prefix byte, the immediately next sibling file entry in the same parent directory with the same decoded filename is the **continuation extent** of this file. The chain continues until an entry with `has_more_extents` = `0` is reached — that entry is the final extent.

### Chain Properties

- All entries in a multi-extent chain share the **same decoded filename** (byte-exact string comparison).
- All extent entries are **consecutive siblings** within their parent directory — no other entries are interleaved between chain members.
- Extents are ordered by ascending byte-offset position within the original file (first extent covers the lowest file offset, last extent covers the highest).
- Each extent entry has its **own string table prefix byte**, encoding that extent's individual `size_width`, `has_checksums`, and `imageid_width` values independently.
- Each extent entry stores its own **individual disc offset** in the entry table's `ImageOffset` field.
- Each extent entry stores its own **individual size** in the string table prefix size field.
- The **last extent** in the chain has `has_more_extents` = `0`.
- A single-extent (non-split) file also has `has_more_extents` = `0`.

### Total Logical File Size

The total logical file size of a multi-extent file is the **sum of the individual extent sizes** read from each entry's string table prefix. There is no single field storing the total size.

### Entry Table Layout Example

A file `video.m2ts` split into 3 extents:

```
Entry i:    Name="video.m2ts", ImageOffset=0x1000, Size=0x8000, has_more_extents=1
Entry i+1:  Name="video.m2ts", ImageOffset=0xF000, Size=0x6000, has_more_extents=1
Entry i+2:  Name="video.m2ts", ImageOffset=0x20000, Size=0x4000, has_more_extents=0
```

Total logical size = 0x8000 + 0x6000 + 0x4000 = 0x12000 bytes.

### Backward Compatibility

The format version remains `1`. Readers unaware of the `has_more_extents` flag (bit 5) will see each extent as an **independent file entry** with the same name. This preserves structural backward compatibility:

- The entry table remains valid — each extent is a standard 12-byte entry with its own `ImageOffset`.
- The string table remains valid — each extent has a decodable prefix byte (bits 0–4 encode size and checksums correctly regardless of bit 5), a valid size field, and a null-terminated name.
- The parent directory's `NextEntryIndex` accounts for all extent entries as distinct children.
- Older readers will produce a file listing with duplicate filenames, one per extent, each showing that extent's individual size — a degraded but non-crashing interpretation.

## Public API Reference

### Properties

#### `EntryCount`
```csharp
public int EntryCount { get; }
```

### Construction Methods

#### `Build`
```csharp
public static NkFs Build(List<FsYamlNode> fileSystems, List<FsYamlIfsEntry>? imageFileSystems = null);
```

#### `FromFsYaml`
```csharp
public static NkFs FromFsYaml(FsYaml fsYaml);
```

### Serialization Methods

#### `ToBytes`
```csharp
public byte[] ToBytes();
```

#### `FromBytes`
```csharp
public static NkFs FromBytes(byte[] data);
```

### Export Methods

#### `ToFsYaml`
```csharp
public FsYaml ToFsYaml();
```

### Navigation Methods

#### `GetEntry`
```csharp
public NkFsEntry GetEntry(int index);
```

#### `GetChildren`
```csharp
public IEnumerable<(int index, NkFsEntry entry)> GetChildren(int directoryIndex);
```

#### `ResolvePath`
```csharp
public int ResolvePath(string path);
```

#### `GetEntryName`
```csharp
public string GetEntryName(int index);
```

#### `GetFileSize`
```csharp
public long GetFileSize(int index);
```
Reads the file size from the string table prefix for a file entry. For multi-extent files, this returns the **individual extent's** size, not the total.

#### `GetTotalFileSize`
```csharp
public long GetTotalFileSize(int index);
```
Returns the total logical file size for a multi-extent file (sum of all extent sizes in the chain). For single-extent files, returns the same value as `GetFileSize`.

#### `GetExtents`
```csharp
public IReadOnlyList<(long offset, long size)> GetExtents(int fileIndex);
```
Returns all extents for the file at the given entry index. For single-extent files, returns one (offset, size) pair. For multi-extent files, resolves the full chain regardless of which entry in the chain is passed, and returns all extents in chain order (first extent first, last extent last).

#### `HasMoreExtents`
```csharp
public bool HasMoreExtents(int index);
```
Returns `true` if bit 5 (has_more_extents) is set in the file entry's prefix byte.

#### `GetChecksums`
```csharp
public (ulong xxHash64, uint crc32) GetChecksums(int index);
```
Reads the xxHash64 and CRC32 checksums from the string table prefix. Returns (0, 0) if no checksums are present.

#### `GetImageIndex`
```csharp
public long GetImageIndex(int index);
```
Reads the image index from the string table prefix for an image file entry.
