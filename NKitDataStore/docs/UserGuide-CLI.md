# NKit DataStore CLI User Guide

Command-line reference for `nkds`, the NKit DataStore filesystem and inspection tool.

---

## Table of Contents

- [Introduction](#introduction)
- [Installation and Invocation](#installation-and-invocation)
- [Global Options](#global-options)
- [Commands](#commands)
  - [create](#create) — Create a new set
  - [add](#add) — Import images through the NKit pipeline
  - [adddir](#adddir) — Store directory contents directly
  - [1gmr](#1gmr) — Batch import via regex routing
  - [list](#list) — List images
  - [sets](#sets) — List sets
  - [stats](#stats) — Show statistics
  - [export](#export) — Export images
  - [verify](#verify) — Verify images
  - [remove](#remove) — Mark images as removed
  - [restore](#restore) — Restore removed images
  - [compact](#compact) — Permanently delete removed data
  - [rollback](#rollback) — Rollback to a specific image
  - [mount](#mount) — Mount as virtual filesystem
  - [version](#version) — Show version
- [Workflow Examples](#workflow-examples)
  - [New Collection Setup](#new-collection-setup)
  - [Redump Import](#redump-import)
  - [Format Conversion](#format-conversion)
  - [Space Management (Remove + Compact)](#space-management-remove--compact)
  - [1GMR Batch Import](#1gmr-batch-import)

---

## Introduction

`nkds` manages NKit DataStore collections from the command line. It handles importing, exporting, verifying, and mounting disc image collections stored in a content-addressed block format with deduplication.

### Core Concepts

| Concept | Description |
|---------|-------------|
| **DataStore** | A directory containing one or more sets. Pass a directory path to `--datastore` when working with multiple sets. |
| **Set** | A `.nkds` index file and its associated shard files. Each set holds a collection of images. Pass a `.nkds` file path to `--datastore` when targeting a specific set. |
| **Image** | A single logical file stored in a set — typically a disc image (ISO, NKit, etc.) or a folder archive. |

A DataStore directory can contain multiple sets, each stored as a `.nkds` index file alongside numbered shard files (`_NNNN.nkds`). Images are deduplicated at the block level across a set, providing significant space savings for collections with shared content.

### Path Resolution

The `--datastore` option accepts either:

- **A directory path** — targets the DataStore as a whole; the tool resolves the appropriate set automatically.
- **A `.nkds` file path** — targets that specific set directly.

---

## Installation and Invocation

The `nkds` tool is distributed as part of the NKit release. Place it on your system PATH or invoke it directly:

```
nkds <command> [options]
```

To get help for any command:

```
nkds <command> help
nkds help <command>
nkds --help
```

---

## Global Options

These options are available across all commands:

| Option | Short | Description |
|--------|-------|-------------|
| `--datastore` | `-ds` | DataStore directory or `.nkds` set path |
| `--help` | `-h`, `-?` | Show help |
| `--version` | `-v` | Show version |

---

## Commands

### create

Create a new set in a DataStore.

**Aliases:** `newset`

**Usage:**

```
nkds create <set-name> [options]
nkds create --datastore <path.nkds> [options]
```

**Options:**

| Option | Short | Description |
|--------|-------|-------------|
| `--datastore` | `-ds` | DataStore directory or `.nkds` set path |
| `--shard-size` | `-ss` | Shard data file size (default: 50GiB; 0 = single file) |
| `--block-size` | `-bs` | Max stored item size (2KiB..2MiB) |
| `--help` | `-h`, `-?` | Show help |

**Examples:**

```bash
# Create a set named "wii/redump.nkds" with default sizes
nkds create --datastore D:\NKitData wii\redump.nkds --shard-size 50GiB --block-size 64KiB

# Create a set by specifying the full .nkds path
nkds create --datastore D:\NKitData\wii\redump.nkds --shard-size 50GiB --block-size 64KiB
```

**Notes:**

- If an aux store exists in the DataStore directory and its block size differs from the requested block size, the tool forces the block size to match the aux store and prints a warning.
- Shard size of `0` creates a single-file (embedded) set with no separate shard files.
- Block size must be between 2 KiB and 2 MiB.

---

### add

Import images through the NKit pipeline into a set.

**Aliases:** `import`

**Usage:**

```
nkds add [options] <input ...>
```

**Options:**

| Option | Short | Description |
|--------|-------|-------------|
| `--datastore` | `-ds` | DataStore directory or `.nkds` set path |
| `--config` | `-cfg` | NKit config file |
| `--input` | `-in` | Add an input file, folder, or mask |
| `--recursive` | `-r` | Scan input folders recursively |
| `--no-archives` | | Skip archive scanning during add |
| `--help` | `-h`, `-?` | Show help |

**Examples:**

```bash
# Import all .rvz files into the DataStore (set resolved automatically)
nkds add --datastore D:\NKitData D:\Roms\*.rvz

# Import into a specific set
nkds add --datastore D:\NKitData\wii\redump.nkds D:\Roms\*.rvz

# Import with a custom NKit config and suppress verification
nkds add --datastore D:\NKitData\wii\redump.nkds -cfg nkit.yaml D:\Roms\*.rvz -v n
```

**Notes:**

- If `--datastore` points to a directory, the target set is resolved automatically.
- If `--datastore` points to a `.nkds` file, that exact set is targeted.
- `--config`, `--recursive`, `--no-archives`, and `--datastore` are translated to NKit parameters.
- Extra NKit command-line parameters are passed through.
- `-task dedupe` is always forced by `nkds add`.

---

### adddir

Store directory contents directly into a set without using the NKit pipeline.

**Usage:**

```
nkds adddir [options] <directory ...>
```

**Options:**

| Option | Short | Description |
|--------|-------|-------------|
| `--datastore` | `-ds` | DataStore directory or `.nkds` set path |
| `--set` | `-s` | Target set name (default: resolved from path or "folders") |
| `--help` | `-h`, `-?` | Show help |

**Examples:**

```bash
# Store a single directory
nkds adddir --datastore D:\NKitData C:\MyFolder

# Store multiple directories into a specific set
nkds adddir --datastore D:\NKitData\wii\redump.nkds C:\Folder1 C:\Folder2

# Store into a named set
nkds adddir --datastore D:\NKitData --set myfolders C:\Data
```

**Notes:**

- Each input directory becomes one Folder image named after the directory.
- Files are stored directly via block deduplication — no NKit pipeline is used.
- If `--set` is omitted, the set is resolved from the `.nkds` set path or defaults to `"folders"`.
- Non-existent input directories are reported as errors; remaining directories are still processed.

---

### 1gmr

Batch import disc images routed by regex masks from a 1GMR YAML file. The name stands for "1 Game, Many ROMs" — each input file is matched against regex patterns to determine which per-game set it belongs to.

**Usage:**

```
nkds 1gmr <yaml-file> [input-paths...] [options]
nkds 1gmr --1gmr <yaml-file> [options]
```

**Options:**

| Option | Short | Description |
|--------|-------|-------------|
| `--1gmr` | | Path to the 1GMR YAML file (alternative to positional) |
| `--datastore` | `-ds` | DataStore directory (must be a directory, not a `.nkds` set path) |
| `--input` | `-in` | Add an input file, folder, or mask |
| `--recursive` | `-r` | Scan input folders recursively |
| `--no-archives` | | Skip archive scanning |
| `--shard-size` | `-ss` | Shard size for new sets (default: 50GiB) |
| `--block-size` | `-bs` | Block size for new sets (default: 64KiB) |
| `--config` | `-cfg` | NKit config file |
| `--help` | `-h`, `-?` | Show help |

**1GMR YAML Format:**

```yaml
games:
  - name: Game Title
    masks:
      - '^Pattern One(?= \(|\.iso|$)'
      - '^Pattern Two(?= \(|\.iso|$)'
  - name: Another Game
    masks:
      - '^Another(?= \(|\.iso|$)'
```

Each entry has a `name` (used as the set name) and `masks` (list of .NET regex patterns). Filenames are matched case-insensitively against masks in order; the first matching game entry wins.

**Examples:**

```bash
# Basic batch import
nkds 1gmr games.yaml D:\Roms\*.rvz --datastore D:\NKitData

# Recursive scan with custom shard size
nkds 1gmr games.yaml D:\Roms\*.rvz -ds D:\NKitData -r

# Using --1gmr flag with explicit input
nkds 1gmr --1gmr games.yaml -in D:\Roms\*.rvz -ds D:\NKitData --shard-size 25GiB
```

**Notes:**

- `--datastore` must point to a directory, not a `.nkds` set path.
- Sets are created automatically using the sanitized game name from the YAML.
- Unmatched files (no regex match) are listed after the import summary.
- Aux store routing is automatic when an aux `.nkds` set exists in the directory.
- Extra NKit command-line parameters are passed through.

---

### list

List images across sets in a DataStore.

**Aliases:** `ls`

**Usage:**

```
nkds list [options]
```

**Options:**

| Option | Short | Description |
|--------|-------|-------------|
| `--datastore` | `-ds` | DataStore directory or `.nkds` set path |
| `--set` | `-s` | Filter by `.nkds` set path |
| `--system` | `-sys` | Filter by system |
| `--search` | `-sch` | Filter by image name text |
| `--format` | `-f` | Output format (`text` or `json`) |
| `--removed` | `-r` | Show removed images |
| `--help` | `-h`, `-?` | Show help |

**Examples:**

```bash
# List all Wii images in the DataStore
nkds list --datastore D:\NKitData --system Wii

# List images in a specific set
nkds list --datastore D:\NKitData\wii\redump.nkds

# Search for a specific title
nkds list --datastore D:\NKitData --search "Mario"

# Output as JSON
nkds list --datastore D:\NKitData --format json

# Include removed images
nkds list --datastore D:\NKitData\wii\redump.nkds --removed
```

---

### sets

List all sets in a DataStore.

**Usage:**

```
nkds sets [options]
```

**Options:**

| Option | Short | Description |
|--------|-------|-------------|
| `--datastore` | `-ds` | DataStore directory |
| `--format` | `-f` | Output format (`text` or `json`) |
| `--help` | `-h`, `-?` | Show help |

**Examples:**

```bash
# List sets in text format
nkds sets --datastore D:\NKitData

# List sets as JSON
nkds sets --datastore D:\NKitData --format json
```

---

### stats

Show statistics for one set or all sets in a DataStore.

**Aliases:** `info`

**Usage:**

```
nkds stats [options]
```

**Options:**

| Option | Short | Description |
|--------|-------|-------------|
| `--datastore` | `-ds` | DataStore directory or `.nkds` set path |
| `--set` | `-s` | `.nkds` set path (optional) |
| `--details` | | Include per-image stats details |
| `--format` | `-f` | Output format (`text`, `json`, or `yaml`) |
| `--help` | `-h`, `-?` | Show help |

**Examples:**

```bash
# Show stats for all sets
nkds stats --datastore D:\NKitData

# Show stats for a specific set with per-image details
nkds stats --set D:\NKitData\wii\redump.nkds --details

# Show stats for a specific set via --datastore
nkds stats --datastore D:\NKitData\wii\redump.nkds

# Output as YAML
nkds stats --datastore D:\NKitData --format yaml
```

---

### export

Export images from a set through the NKit pipeline.

**Aliases:** `copy`, `convert`

**Usage:**

```
nkds export --datastore <path.nkds> --mask <mask> --output <folder> [options]
```

**Options:**

| Option | Short | Description |
|--------|-------|-------------|
| `--datastore` | `-ds` | `.nkds` set path |
| `--mask` | `-m` | Image mask within the set |
| `--output` | `-out` | Output folder |
| `--format` | `-f` | Optional NKit convert format value |
| `--config` | `-cfg` | NKit config file |
| `--help` | `-h`, `-?` | Show help |

**Examples:**

```bash
# Export all ISOs to a folder (full stored format)
nkds export --datastore D:\NKitData\wii\redump.nkds --mask *.iso -out C:\Temp

# Export and convert to WUX format
nkds export --datastore D:\NKitData\wii\redump.nkds --mask *.iso -out C:\Temp --format wux

# Export with RVZ compression settings and custom config
nkds export --datastore D:\NKitData\wii\redump.nkds --mask *.iso -out C:\Temp --format rvz:zstd:19:128k:16 -cfg nkit.yaml
```

**Notes:**

- If `--format` is omitted, `nkds export` uses the full stored format via `-task expand`.
- If `--format` is specified, `nkds export` uses `-task convert -convert <value>`.
- Extra NKit command-line parameters are passed through.

---

### verify

Verify images through the NKit pipeline.

**Aliases:** `vfy`

**Usage:**

```
nkds verify --datastore <path.nkds> --mask <mask> [options]
```

**Options:**

| Option | Short | Description |
|--------|-------|-------------|
| `--datastore` | `-ds` | `.nkds` set path |
| `--mask` | `-m` | Image mask within the set |
| `--config` | `-cfg` | NKit config file |
| `--help` | `-h`, `-?` | Show help |

**Examples:**

```bash
# Verify all ISO images in a set
nkds verify --datastore D:\NKitData\wii\redump.nkds --mask *.iso

# Verify with a custom NKit config
nkds verify --datastore D:\NKitData\wii\redump.nkds --mask *.iso -cfg nkit.yaml
```

**Notes:**

- `-task verify` and `-v y` are always forced by `nkds verify`.
- Extra NKit command-line parameters are passed through.

---

### remove

Mark images as removed within a set. Removed images no longer appear in views but their data remains on disk until compaction.

**Aliases:** `rm`

**Usage:**

```
nkds remove --datastore <path.nkds> [image_id...]
```

**Options:**

| Option | Short | Description |
|--------|-------|-------------|
| `--datastore` | `-ds` | `.nkds` set path |
| `--help` | `-h`, `-?` | Show help |

**Examples:**

```bash
# Remove images with IDs 1, 2, and 3
nkds remove --datastore D:\NKitData\wii\redump.nkds 1 2 3
```

**Notes:**

- This flags image records so they no longer appear in views.
- Use the `compact` command afterwards to permanently clean up data.
- Use `list --removed` to see which images have been marked for removal.
- Use `restore` to undo a removal before compaction.

---

### restore

Restore images previously marked as removed within a set.

**Aliases:** `rst`

**Usage:**

```
nkds restore --datastore <path.nkds> [image_id...]
```

**Options:**

| Option | Short | Description |
|--------|-------|-------------|
| `--datastore` | `-ds` | `.nkds` set path |
| `--help` | `-h`, `-?` | Show help |

**Examples:**

```bash
# Restore images with IDs 1, 2, and 3
nkds restore --datastore D:\NKitData\wii\redump.nkds 1 2 3
```

---

### compact

Permanently delete marked images and unused blocks from a set. This reclaims disk space by removing data that is no longer referenced.

**Aliases:** `cp`

**Usage:**

```
nkds compact --datastore <path.nkds>
```

**Options:**

| Option | Short | Description |
|--------|-------|-------------|
| `--datastore` | `-ds` | `.nkds` set path |
| `--help` | `-h`, `-?` | Show help |

**Examples:**

```bash
nkds compact --datastore D:\NKitData\wii\redump.nkds
```

**Notes:**

- Only operates on images previously marked as removed.
- This operation is irreversible — compacted data cannot be recovered.
- Run `remove` first to mark images, then `compact` to reclaim space.

---

### rollback

Rollback a set to a specific image, undoing all changes made after that image was added.

**Usage:**

```
nkds rollback <image_id> [options]
nkds rollback [options]
```

**Options:**

| Option | Short | Description |
|--------|-------|-------------|
| `--datastore` | `-ds` | `.nkds` set path |
| `--help` | `-h`, `-?` | Show help |

**Examples:**

```bash
# List images to find the rollback target (use list command)
nkds list --datastore D:\NKitData\wii\redump.nkds

# Rollback to image 42
nkds rollback --datastore D:\NKitData\wii\redump.nkds 42

# Show rollback info without specifying an ID
nkds rollback --datastore D:\NKitData\wii\redump.nkds
```

**Notes:**

- Call rollback without an ID or use the `list` command to list images ordered by their insertion.
- Rollback removes all images added after the specified image ID.

---

### mount

Mount a DataStore or set as a virtual filesystem, allowing disc images to be accessed as regular files without extraction.

**Usage:**

```
nkds mount [options]
```

**Options:**

| Option | Short | Description |
|--------|-------|-------------|
| `--datastore` | `-ds` | DataStore directory or `.nkds` set path |
| `--mount` | `-m` | Mount point |
| `--image` | `-i` | Show full images as files (e.g. .iso) |
| `--filesystem` | `-fs` | Show filesystem tree folders per image |
| `--system` | `-s` | Show system entries, headers, and stored files (e.g. filesystem.yaml) for supported systems |
| `--update` | `-u` | Update mode: enable rename support for images and app folders |
| `--allow-other` | | Allow other users to access the mount (Linux only) |
| `--uid` | | Override file owner UID/User in the mount (Linux only) |
| `--gid` | | Override group owner GID/Group in the mount (Linux only) |
| `--help` | `-h`, `-?` | Show help |

**View Flags:**

When none of `--image`, `--filesystem`, or `--system` are specified, the mount shows images and filesystem folders (the default behaviour). When one or more flags are given, only the selected views are shown.

**Examples:**

```bash
# Mount entire DataStore at a drive letter (Windows)
nkds mount --datastore D:\NKitData --mount N:\

# Mount a specific set
nkds mount --datastore D:\NKitData\wii\redump.nkds --mount N:\

# Mount with only image and filesystem views
nkds mount -ds D:\NKitData -m N:\ -i -fs

# Mount with filesystem and system views
nkds mount -ds D:\NKitData -m N:\ -fs -s

# Mount with UID override (Linux)
nkds mount -ds /var/lib/nkitdata -m /mnt/nkit --uid 1000

# Mount with user/group name override (Linux)
nkds mount -ds /var/lib/nkitdata -m /mnt/nkit --uid root --gid root
```

**Notes:**

- `--allow-other` is automatically enabled if `--uid` or `--gid` is specified.
- The mount runs in the foreground; press Enter to unmount.
- On Linux, requires FUSE support. On Windows, requires WinFsp.

---

### version

Show version information.

**Usage:**

```
nkds version
nkds --version
```

---

## Workflow Examples

### New Collection Setup

Create a new DataStore and set, then import your first images:

```bash
# 1. Create a set for Wii Redump images
nkds create --datastore D:\NKitData wii\redump.nkds --shard-size 50GiB --block-size 64KiB

# 2. Import disc images
nkds add --datastore D:\NKitData\wii\redump.nkds D:\Roms\Wii\*.rvz

# 3. Verify the import
nkds stats --datastore D:\NKitData\wii\redump.nkds

# 4. Mount and browse
nkds mount --datastore D:\NKitData --mount N:\
```

### Redump Import

Import a full Redump collection with verification:

```bash
# Import all images recursively from a Redump folder
nkds add --datastore D:\NKitData\wii\redump.nkds D:\Redump\Wii\ -r

# Verify all imported images
nkds verify --datastore D:\NKitData\wii\redump.nkds --mask *.iso

# Check statistics to confirm deduplication savings
nkds stats --datastore D:\NKitData\wii\redump.nkds --details
```

### Format Conversion

Export images in a different format for use with emulators:

```bash
# Export as WUX (compressed Wii U format)
nkds export --datastore D:\NKitData\wiiu\redump.nkds --mask *.iso -out C:\Emulator\Games --format wux

# Export as RVZ with specific compression settings
nkds export --datastore D:\NKitData\wii\redump.nkds --mask *.iso -out C:\Dolphin\Games --format rvz:zstd:19:128k:16

# Export in original stored format (no conversion)
nkds export --datastore D:\NKitData\wii\redump.nkds --mask "Mario*.iso" -out C:\Temp
```

### Space Management (Remove + Compact)

Remove unwanted images and reclaim disk space:

```bash
# List images to find IDs
nkds list --datastore D:\NKitData\wii\redump.nkds

# Mark images for removal
nkds remove --datastore D:\NKitData\wii\redump.nkds 5 12 37

# Verify they're marked (shows in removed list)
nkds list --datastore D:\NKitData\wii\redump.nkds --removed

# Optionally restore one before compacting
nkds restore --datastore D:\NKitData\wii\redump.nkds 12

# Permanently reclaim space
nkds compact --datastore D:\NKitData\wii\redump.nkds

# Confirm space savings
nkds stats --datastore D:\NKitData\wii\redump.nkds
```

### 1GMR Batch Import

Organize a large collection into per-game sets using a YAML routing file:

**Step 1:** Create a `games.yaml` file defining your game patterns:

```yaml
games:
  - name: Super Mario Galaxy
    masks:
      - '^Super Mario Galaxy(?= \(|\.iso|$)'
  - name: Zelda Twilight Princess
    masks:
      - '^Legend of Zelda.*Twilight(?= \(|\.iso|$)'
  - name: Metroid Prime 3
    masks:
      - '^Metroid Prime 3(?= \(|\.iso|$)'
```

**Step 2:** Run the batch import:

```bash
# Import with automatic set creation per game
nkds 1gmr games.yaml D:\Roms\*.rvz --datastore D:\NKitData

# Recursive scan with custom shard size
nkds 1gmr games.yaml D:\Roms\ -ds D:\NKitData -r --shard-size 25GiB
```

**Step 3:** Review results:

```bash
# List all created sets
nkds sets --datastore D:\NKitData

# Check stats across all sets
nkds stats --datastore D:\NKitData
```

The tool reports a summary showing total images processed, how many were imported, how many were unmatched (no regex hit), and how many failed. Unmatched filenames are listed at the end so you can update your YAML patterns.
