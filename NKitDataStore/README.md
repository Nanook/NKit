# NKitDataStore

A deduplication archiver for disc images — content-addressed block storage that scales from small per-game collections to multi-TiB (and beyond) preservation libraries. Designed with sequential I/O patterns that perform well over both local and network storage (SMB/NFS).

## Key Benefits

- **~80% space savings** — Automatic deduplication and Zstd compression dramatically reduce storage for disc image collections where games share common data
- **Mountable VFS** — Images can be mounted as virtual filesystems via FUSE, no extraction required
- **Content-aware** — Native understanding of Wii, GameCube, and Wii U disc structures including strided sector layouts
- **Crash-safe** — Dual-header atomic commit with copy-on-write ensures data integrity even during power loss

## Core Concepts

| Concept | Description |
|---------|-------------|
| **DataStore** | A directory containing one or more sets. Created by pointing `new DataStore(path)` at a directory. |
| **Set** | A `.nkds` index file and its associated shard files, forming a logical container for images. Each set has its own block size and shard configuration. Sets can contain images from any system mixed together — there is no requirement to separate by platform. |
| **Image** | A single logical file stored in a set (disc image, folder archive, etc.). Images are composed of areas, which reference blocks. |
| **Block** | A fixed-size (default 64 KiB) chunk of data, content-addressed by XXHash64 + CRC32. Identical blocks across images are stored once. |
| **Shard** | A numbered file (`{setName}_NNNN.nkds`) containing compressed block payloads. New shards are created when the current one reaches the configured size limit. |
| **Area** | A logical region within a disc image (e.g., data partition, system area). Areas define the byte range, hashes, and optional stride parameters for their region. |
| **Stride** | Describes the physical layout of data within sectors that include padding, headers, or hashes. For example, Wii disc sectors are 0x8000 bytes with 0x400 bytes of hashes followed by 0x7C00 bytes of user data. Stride parameters allow the store to handle these formats natively. |
| **Aux Store** | A companion set providing fallback block data for images with missing blocks. Named `{setName}.aux` and automatically discovered during reads. |

## Features

- **Content-addressed block storage** — Data is split into fixed-size blocks identified by XXHash64 + CRC32 keys
- **Automatic deduplication** — Identical blocks across images are stored once
- **Zstd compression** — All metadata and blocks are compressed (level 19 for index data)
- **Append-only writes** — Crash-safe design with dual headers and atomic commit
- **Two storage modes** — Separate mode (multi-file with configurable shard sizes) or embedded mode (single-file)
- **Two-part image sections** — Lightweight metadata (for listings) separate from heavy block maps (for reads)
- **VFS mounting** — Images can be mounted as virtual filesystems via FUSE
- **Soft-delete and restore** — Images can be removed and recovered without rewriting the file
- **Compaction** — Crash-safe space reclamation from deleted images with block index pruning and shard defragmentation
- **Deterministic compaction** — Optional mode producing byte-identical output for equivalent inputs
- **Auxiliary (aux) stores** — Companion stores providing fallback block data for images with missing blocks
- **Sequential I/O** — All read operations use sequential access patterns, no random seeks within image data

## Storage Modes

### Separate Mode (shardSize > 0)

Block data is stored in numbered shard files alongside the index. When a shard reaches the configured size limit, a new shard is created. This is the default and recommended mode for large collections scaling to many TiB.

```
wii.nkds          ← Binary index (metadata, directory, block index)
wii_0000.nkds     ← Shard 0 (block payloads, up to shardSize)
wii_0001.nkds     ← Shard 1 (overflow)
```

### Embedded Mode (shardSize = 0)

Everything lives in a single `.nkds` file — block data at the front, binary index appended at the end, with a 12-byte footer. Best suited for smaller sets (e.g., per-game 1GMR collections) where single-file simplicity is preferred. Requires extraction during writes and compaction, so not ideal for very large sets.

```
test.nkds
├── [0, Shard_Boundary)         Block data (shard region)
├── [Shard_Boundary, EOF-12)    Binary index
└── [EOF-12, EOF)               Embedded footer (IndexSize + "NKDS" magic)
```

## File Naming Conventions

Each set produces the following files in the base directory:

| Pattern | Description |
|---------|-------------|
| `{setName}.nkds` | Binary index file (or embedded file in embedded mode) |
| `{setName}_0000.nkds` | First shard file (compressed block payloads) |
| `{setName}_0001.nkds` | Second shard file (when first reaches shard size limit) |
| `{setName}_NNNN.nkds` | Subsequent shard files (4-digit decimal suffix) |
| `{setName}.aux.nkds` | Auxiliary store index (optional, for fallback blocks) |
| `{setName}.aux_0000.nkds` | Auxiliary store shard (optional) |

Example for a set named "wii" with 50 GB shards:
```
D:\MyData\
├── wii.nkds          ← Binary index (metadata + block index)
├── wii_0000.nkds     ← Shard 0 (block payloads, up to 50 GB)
├── wii_0001.nkds     ← Shard 1
└── wii_0002.nkds     ← Shard 2
```

## Configuration Options

| Parameter | Default | Description |
|-----------|---------|-------------|
| `shardSize` | 50 GiB | Maximum size of each shard file in bytes. Use `0` for embedded mode (single file, block data + index together). Embedded mode is best for smaller sets like per-game 1GMR collections; separate mode is recommended for large-scale archiving. |
| `blockSize` | 64 KiB (`0x10000`) | Size of each content-addressed block. Determines deduplication granularity. |
| `maxOffsetBlocks` | 336 | Maximum number of block keys per offset record. |

These values are stored in the binary index file header and are immutable after set creation.

## Thread Safety Model

The concurrency model is designed around per-set isolation with lock-free reads:

- **Multiple concurrent readers** — Any number of readers can access a set simultaneously. Readers use `ConcurrentDictionary`-based caches for metadata and block maps, requiring no exclusive locks.
- **Single writer per set** — Only one writer may be registered at a time per set, enforced by `RegisterWriter` with a per-set `SetUsageInfo` lock. Attempting to register a second writer throws `InvalidOperationException`.
- **Concurrent read + write** — Readers see the last committed state while a writer appends new data. The dual-header atomic commit makes new data visible to subsequent readers only after a successful commit.
- **Per-set write locks** — A `SemaphoreSlim` per set serializes write operations (compaction, transactions) to prevent concurrent structural modifications.
- **Directory access** — The in-memory image directory is replaced atomically (reference swap) on commit. Existing readers continue using their snapshot safely.
- **Per-set isolation** — Each set has independent index files, shard managers, and caches. Operations on one set do not block operations on another.

## Quick Start

### Create a DataStore

```csharp
using NKitDataStore;

// Point to a directory where sets will be stored
using var dataStore = new DataStore(@"D:\MyData");
```

### Create a Set

A set is a logical container for images. Each set has its own `.nkds` index file and shard files.

```csharp
// Separate mode: 50 GB shard size, default 64 KiB block size
SetInfo setInfo = dataStore.CreateSet("wii", shardSize: 50L * 1024 * 1024 * 1024);

// Separate mode: custom block size
SetInfo customSet = dataStore.CreateSet("gamecube", shardSize: 50L * 1024 * 1024 * 1024, blockSize: 0x8000);

// Embedded mode: single file (block data + index in one .nkds file)
SetInfo embeddedSet = dataStore.CreateSet("small-collection", shardSize: 0);
```

### Add an Image (Write)

```csharp
using IImageWriter writer = dataStore.AddImage("wii", "MyGame.iso", system: "Wii", format: ImageFormat.Iso);

// Write data through the writer's stream or block API
// The writer handles block splitting, hashing, deduplication, and compression
writer.WriteArea(offset: 0, size: imageSize, crc32: crc, xxhash64: hash);
// ... write blocks and offsets ...

writer.Commit(); // Atomic commit — all or nothing
```

### Open an Image (Read)

```csharp
var key = new GlobalImageKey("wii", imageId: 1);
using IImageReader reader = dataStore.OpenImageReader(key);

// Access image metadata
ImageRecord image = reader.Image;
Console.WriteLine($"Name: {image.Name}, Size: {image.Size}");

// Read areas, offsets, and block data through the reader
IEnumerable<AreaRecord> areas = reader.GetAreas();
```

### Delete and Restore Images

```csharp
var key = new GlobalImageKey("wii", imageId: 3);

// Soft-delete (marks as removed, data preserved)
dataStore.DeleteImage(key);

// Restore a previously deleted image
dataStore.RestoreImage(key);
```

### Rename an Image

```csharp
var key = new GlobalImageKey("wii", imageId: 3);
ImageRecord renamed = dataStore.RenameImage(key, "New Name.iso");
```

### Rollback

```csharp
// Roll back to image 5 — all images with ID > 5 are marked as removed
var key = new GlobalImageKey("wii", imageId: 5);
dataStore.RollbackImage(key);
```

### Compact a Set

```csharp
// Permanently removes deleted images, prunes orphaned blocks, and merges delta indexes
dataStore.CompactSet("wii");

// With progress reporting
dataStore.CompactSet("wii", progress: new Progress<(int Percentage, string Stage)>(p =>
    Console.WriteLine($"{p.Percentage}% — {p.Stage}")));
```

Compaction is crash-safe — an interrupted compact can be recovered on the next open. See [docs/COMPACTION.md](docs/COMPACTION.md) for full details.

### Get Statistics

```csharp
DataStoreStatistics? stats = dataStore.GetSetStatistics("wii", includePerImageStats: true,
    progress: (processed, total) => Console.WriteLine($"{processed}/{total}"));

if (stats != null)
{
    Console.WriteLine($"Total images: {stats.ImageCount}");
    Console.WriteLine($"Dedup ratio: {stats.DeduplicationRatio:P1}");
}
```

### List Sets and Images

```csharp
// List all set names
IEnumerable<string> setNames = dataStore.ListSetNames();

// List all images across all sets
IEnumerable<ImageRecord> allImages = dataStore.ListAllImages();

// List images in a specific set
List<ImageRecord> wiiImages = dataStore.ListImagesInSet("wii");

// Get set info with file paths
SetInfo? info = dataStore.GetSetInfo("wii");
```

### Check Set Health

```csharp
// Inspect file state without modifying anything
RecoveryState health = dataStore.CheckSetHealth("wii");
```

### Read Auxiliary Files

```csharp
// Read a stored file (e.g., filesystem.nkfs) without opening a full image reader
var key = new GlobalImageKey("wii", imageId: 1);
byte[]? fsData = dataStore.ReadFile(key, "filesystem.nkfs");
```

## Auxiliary (Aux) Stores

An aux store is a companion set that provides fallback block data for images whose primary blocks are incomplete. Aux stores follow the naming convention `{setName}.aux`:

```
D:\MyData\
├── wii.nkds           ← Primary set index
├── wii_0000.nkds      ← Primary shard
├── wii.aux.nkds       ← Aux set index
└── wii.aux_0000.nkds  ← Aux shard (fallback blocks)
```

When reading an image, the `AuxBlockProvider` automatically discovers the aux store and falls back to it for any blocks not found in the primary store. Aux stores must have the same block size as the primary store.

## 1GMR (1 Game, Many ROMs)

1GMR is a workflow for organizing large collections into per-game sets. The name stands for "1 Game, Many ROMs" — the idea is that a single game title often has many regional variants, revisions, and demo versions. Rather than storing all images in one massive set, 1GMR routes each file into a dedicated per-game set based on regex pattern matching.

A YAML file defines the routing rules:

```yaml
games:
  - name: Super Mario Galaxy
    masks:
      - '^Super Mario Galaxy(?= \(|\.iso|$)'
  - name: Zelda Twilight Princess
    masks:
      - '^Legend of Zelda.*Twilight(?= \(|\.iso|$)'
```

Each game gets its own `.nkds` set. This pairs naturally with embedded mode (`shardSize = 0`) — each game becomes a single self-contained file that's easy to copy, back up, or share. Regional variants of the same game typically share significant block content, so deduplication within each per-game set is highly effective.

Use the `nkds 1gmr` command (CLI) or the **Add 1GMR** toolbar button (UI) to run a batch import. See the [CLI User Guide](docs/UserGuide-CLI.md#1gmr) for full details.

## Dat Verification

Dat files are XML catalogs of expected ROMs published by preservation groups like Redump and No-Intro. They define the canonical set of known good dumps for a system — each entry specifies a filename and CRC/hash that a correct dump should have.

NKit DataStore can verify your collection against a dat file to answer: which images do I have, which am I missing, and which have problems? The verification classifies each entry as:

- **Correct** — Image matches by name and CRC
- **Missing** — Dat entry has no matching image in the set
- **Badly Named** — Image matches by CRC but has the wrong filename (can be auto-renamed)
- **Wrong CRC** — Image matches by name but has a different CRC
- **Unmatched** — Image exists in the set but has no corresponding dat entry

Use the **Dat** toolbar button in the UI to load a dat file and verify interactively, with options to filter by category and auto-rename badly-named images. See the [UI User Guide](docs/UserGuide-UI.md#dat-verification) for details.

## Target Frameworks

- .NET 10.0
- .NET 8.0
- .NET Standard 2.1

## Dependencies

- **[GrindCore](https://www.nuget.org/packages/GrindCore) 0.7.1** — Provides XXHash64, CRC32, and Zstd compression/decompression

## Documentation

| Document | Description |
|----------|-------------|
| [Format Specification](docs/FORMAT.md) | High-level binary format overview, operations, and crash safety |
| [CLI User Guide](docs/UserGuide-CLI.md) | Complete command-line reference with examples |
| [UI User Guide](docs/UserGuide-UI.md) | GUI application feature guide |
| [Compaction](docs/COMPACTION.md) | Compaction lifecycle, shard defragmentation, and crash recovery |
| [Recovery](docs/RECOVERY.md) | Crash recovery states and dual-header protocol |
| [NkFs Format](docs/NkFsFormat.md) | Virtual filesystem format for mounted images |
