# NKit DataStore — UI User Guide

NKit DataStore includes a graphical application for managing, analysing, and exporting disc images stored in DataStore sets. This guide covers the application layout, every toolbar operation, analytical features, and supporting dialogs.

## Table of Contents

- [Application Layout](#application-layout)
- [Toolbar Operations](#toolbar-operations)
  - [File Operations](#file-operations)
  - [Import Operations](#import-operations)
  - [Lifecycle Operations](#lifecycle-operations)
  - [Analytics Operations](#analytics-operations)
  - [Mount](#mount)
  - [Grouping](#grouping)
- [Image List](#image-list)
- [Filter Bar and Search](#filter-bar-and-search)
- [Analytical Features](#analytical-features)
  - [Threshold Grouping](#threshold-grouping)
  - [Dat Verification](#dat-verification)
  - [Graphs](#graphs)
- [Export Format Options](#export-format-options)
- [Mount Dialog](#mount-dialog)
- [Progress Panels](#progress-panels)

---

## Application Layout

The main window is divided into the following areas:

- **Toolbar** — A horizontal bar at the top containing grouped operation buttons and a set selector dropdown. Operations are organised into logical groups: file management, import, lifecycle, analytics, mount, and grouping.
- **Set Selector** — A dropdown within the toolbar that selects the active set. When "All" is selected, images from every open set are shown. Individual set names target operations at a specific set.
- **Image List** — The central area displaying a sortable, multi-select data grid of all images in the active set (or all sets). Columns include name, system, format, size, and verification status.
- **Filter Bar** — An optional bar (toggled via the toolbar) providing text search and system filter controls to narrow the image list.
- **Progress Panels** — Panels that appear at the bottom during long-running operations, showing operation name, current item, percentage, elapsed time, and a cancel button.

---

## Toolbar Operations

The toolbar contains the following operations, organised into groups. Each button has a label, icon, and tooltip describing its purpose.

### File Operations

| Button | Purpose |
|--------|---------|
| **Open** | Open all sets in a DataStore directory. Presents a folder picker to select the DataStore root directory. |
| **Open Set** | Open a specific `.nkds` set file directly. Presents a file picker. |
| **Close** | Close the currently open DataStore or set. Resets the image list and filter state. |
| **Create Set** | Create a new empty set in the open DataStore. Opens a dialog to specify the set name, shard size, block size, and optional aux mode. |

### Import Operations

| Button | Purpose |
|--------|---------|
| **Add** | Import images into the active set. Opens a dialog to select files and target set. Images are processed through the NKit pipeline (content detection, block splitting, deduplication). Progress is shown per-image with step names. |
| **Add Dir** | Archive a regular directory tree into the active set. Opens a folder picker; the selected directory is stored as a single image. |
| **Add 1GMR** | Import related images using the "1 Game, Many ROMs" workflow. Opens a dialog to configure a YAML routing file, output folder, shard/block sizes, and aux mode. Files are matched to set names by regex rules and imported in batch. |

### Lifecycle Operations

| Button | Purpose |
|--------|---------|
| **Verify** | Check integrity of selected images (or all images in the active set if none selected). Reads and validates block data against stored hashes. Per-image results are shown as pass/fail status in the image list. |
| **Export** | Export selected images to files on disk. Opens the Export dialog where you choose an output directory and target format per system/source combination. |
| **Remove** | Soft-delete selected images from the set. Operates immediately on selected non-removed images (no confirmation dialog). Removed images remain in the set until compacted and can be restored. |
| **Restore** | Restore previously removed images. Operates on selected removed images, reversing the soft-delete. |
| **Compact** | Permanently reclaim disk space by purging removed image data and defragmenting the index. Opens a dialog to select which sets to compact. This operation is irreversible for removed images. |
| **Rollback** | Undo recent additions to the active set. Opens a dialog showing all images in the set; select a target image to roll back to (all images added after the target are removed). |

### Analytics Operations

| Button | Purpose |
|--------|---------|
| **Stats** | Calculate storage statistics and ratios for the active set. Computes deduplication savings, compression ratios, and per-image storage metrics. Results appear as additional columns in the image list. |
| **Graph** | Show storage statistics as visual graphs. If stats have already been computed, the graph displays immediately; otherwise stats are calculated first. Opens a separate graph dialog. |
| **Dat** | Open the Dat Verification window. Loads a dat file (XML catalog of expected ROMs) and verifies images in the active set against it, reporting correct, missing, badly-named, wrong-CRC, and unmatched entries. |
| **YAML** | Export the currently visible (filtered) image list data to a YAML file. Opens a save file picker; the exported file includes image metadata and optionally stats columns if computed. |

### Mount

| Button | Purpose |
|--------|---------|
| **Mount** | Mount the listed images as a virtual filesystem. Opens the Mount dialog to configure the mount point and view options. While mounted, the button label changes to "Unmount" and mutating operations on the mounted set are disabled. Clicking again unmounts. |

### Grouping

| Button | Purpose |
|--------|---------|
| **Grouping** | Open the Threshold Grouping window. Computes pairwise similarity between all filtered images and groups them by a configurable similarity threshold. |

---

## Image List

The image list is the central data grid showing all images matching the current set selection and filters.

**Columns** include:
- Name — The image filename
- System — The detected system (GameCube, Wii, WiiU, Directories, etc.)
- Format — The source format of the stored image
- Size — The logical size of the image
- Set — The set the image belongs to (visible when viewing "All" sets)
- Verification status — Pass/fail indicator after running Verify

**Interactions:**
- Click a column header to sort (cycles through ascending → descending → default order)
- Select one or more images using standard multi-select (Ctrl+click, Shift+click)
- Right-click context menus may provide quick access to operations

---

## Filter Bar and Search

The filter bar provides controls to narrow the image list without modifying the underlying data.

**Text Filter (Name Search):**
- Case-insensitive substring match on image names
- Type to filter in real-time; the image list updates as you type

**System Filter:**
- A dropdown populated with all distinct system values from the current images
- Select a system to show only images of that type
- Clear the selection to show all systems

**Set Filter:**
- Controlled by the set selector in the toolbar
- When a specific set is selected, only images from that set are shown
- "All" shows images from every open set

The filter bar visibility is toggled from the toolbar. All filters combine (AND logic): an image must match the text filter, system filter, and set filter to appear.

---

## Analytical Features

### Threshold Grouping

The Threshold Grouping window computes pairwise similarity between all currently filtered images and groups them by a configurable similarity threshold. It opens as a separate window from the **Grouping** toolbar button.

**How it works:**
1. Click the **Grouping** toolbar button to open the window (requires at least 2 filtered images)
2. Configure the similarity threshold (default 60%) and thread count
3. Click **Start** to begin computation — the window loads block hashes into memory, then computes pairwise similarity across all filtered images
4. Results show groups of images that share content above the threshold

**Two-phase computation:**
- Phase 1 (0–50% progress): Load block hashes for all filtered images into an in-memory cache
- Phase 2 (50–100% progress): Compute pairwise similarity between all loaded images

**Configuration:**
- **Threshold** — Minimum similarity percentage to consider two images as related (adjustable with +/− buttons; results re-filter instantly without recomputation)
- **Threads** — Number of parallel threads for computation (up to 2× CPU cores)

**Results display:**
- Header summary with total groups and total grouped images
- Numbered group cards showing member images with min/max/avg match percentages
- Each image within a group shows its best match percentage

**Toolbar buttons:**
- **Start** — Begin the computation
- **Cancel** — Abort a running computation
- **Save** — Export grouped results as YAML
- **Export** — Save the full pairwise similarity matrix for later import
- **Import** — Load a previously exported pairwise matrix to avoid recomputation

**Re-filtering:**
After computation completes, adjusting the threshold re-filters the existing results instantly without recomputing. This allows exploring different grouping thresholds from a single computation run.

**Window lifecycle:**
The Grouping window operates independently of the main window. Closing it releases the in-memory hash cache. The toolbar button shows a checked state while the window is open.

### Dat Verification

The Dat Verification window verifies images in the active set against a dat file (an XML catalog of expected ROMs, typically from Redump or No-Intro).

**Workflow:**
1. Click the **Dat** toolbar button to open the window
2. Browse for or enter the path to a dat file (history dropdown available)
3. Click **Load** to parse the dat file
4. Verification runs automatically against the active set

**Result categories:**
- **Correct** — Image matches a dat entry by name and CRC
- **Missing** — Dat entry has no matching image in the set
- **Badly Named** — Image matches by CRC but has the wrong filename
- **Wrong CRC** — Image matches by name but has a different CRC
- **Unmatched** — Image exists in the set but has no corresponding dat entry

**Features:**
- Summary counts for each category (invariant to filtering)
- Filter toggles to show/hide each category in the results grid
- Sortable results grid (by status, name, etc.)
- **Rename** button — Automatically rename badly-named images to match their dat entry names
- Re-verifies automatically when the active set changes
- Dat path history for quick re-selection

### Graphs

The Graphs feature visualises storage statistics for the active set.

**How it works:**
1. Click the **Graph** toolbar button
2. If stats have not been computed yet, the application calculates them first (with progress)
3. A graph dialog opens showing visual representations of storage data

**Statistics computed include:**
- Per-image storage breakdown
- Compression ratios
- Deduplication savings
- Block reference distribution

The computation can be cancelled via the progress panel's cancel button.

---

## Export Format Options

The Export dialog allows you to choose target formats for each system and source format combination found in your selection.

**Available format conversions:**

| System | Source | Available Targets |
|--------|--------|-------------------|
| GameCube | ISO | ISO, RVZ, CISO, WBFS |
| Wii | ISO | ISO, RVZ, CISO, WBFS |
| WiiU | ISO | ISO, WUX, APP |
| WiiU | APP | APP |
| Directories | Folder | DIR (extract to directory) |
| Other | Any | Same format (no conversion) |

**Format-specific options:**
- **RVZ** — Encoding (e.g., zstd, lzma), compression level, and block size
- **CISO / WBFS** — Lossless mode toggle

**Dialog features:**
- Output directory selection with browse button and MRU history
- Per-system format grid showing one row per unique (System, Source Format) pair
- Format preferences are persisted between sessions
- Selected image count displayed for reference

---

## Mount Dialog

The Mount dialog configures how images are exposed as a virtual filesystem.

**Options:**

| Option | Description |
|--------|-------------|
| **Mount Point** | Directory path where the filesystem will appear (max 260 characters). Browse button and MRU history available. |
| **Show Image** | Include the image-level view in the mounted filesystem (enabled by default) |
| **Show FileSystem** | Include the filesystem-level view (enabled by default) |
| **Show System** | Include the system-level view |
| **Update Mode** | Mount in update mode (mutually exclusive with the view options above) |

**Linux-specific options (FUSE):**
- **Allow Other** — Allow other users to access the mount (`allow_other` FUSE option)
- **UID** — Override file owner user ID
- **GID** — Override file owner group ID

Setting UID or GID automatically enables Allow Other.

**Platform support:**
- On Windows, requires the Dokan driver
- On Linux, uses FUSE
- If the platform is unsupported, the dialog shows a warning and disables mount configuration

**Toggle behaviour:**
While a set is mounted, the Mount toolbar button shows "Unmount" and displays the active mount point in its tooltip. Clicking it again unmounts the filesystem. Mutating operations (Add, Remove, Compact, etc.) are disabled on the mounted set while the mount is active.

---

## Progress Panels

Long-running operations display a progress panel at the bottom of the main window.

**Panel contents:**
- Operation name (e.g., "Add Images", "Compact", "Verify")
- Current item being processed
- Progress percentage bar
- Items processed / total items count
- Elapsed time
- **Cancel** button to abort the operation

**Behaviour:**
- Multiple operations can show progress simultaneously (each gets its own panel)
- Panels remain visible briefly after completion before being removed
- Cancelling an operation stops processing and reports the cancellation
- The toolbar disables conflicting operations while a mutating operation is in progress on a set

Operations that show progress panels include: Add, Add Dir, Add 1GMR, Verify, Export, Remove, Restore, Compact, Rollback, Stats, and Graphs.
