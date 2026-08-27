<p align="center">
  <img src="NKit-Banner.jpg" alt="NKit" />
</p>

<h1 align="center">NKit & NKDS</h1>

<p align="center">
  <strong>Multiplatform disc image processor & deduplicated game storage</strong><br/>
  Convert, extract, scan, verify, fix, and deduplicate disc images across 14 gaming systems.<br/>
  Then store your entire collection at ~80% space savings with instant virtual-drive mounting.
</p>

<p align="center">
  <a href="https://discord.gg/YT792u5yWJ"><img src="https://img.shields.io/badge/Discord-Join%20Us-5865F2?logo=discord&logoColor=white" alt="Discord" /></a>
  <a href="https://ko-fi.com/nanook_"><img src="https://img.shields.io/badge/Ko--fi-Buy%20Me%20a%20Beer-FF5E5B?logo=kofi&logoColor=white" alt="Ko-fi" /></a>
  <a href="https://ko-fi.com/nanook_/tiers"><img src="https://img.shields.io/badge/Ko--fi-Sponsor%20Me-FF5E5B?logo=kofi&logoColor=white" alt="Sponsor" /></a>
  <a href="https://www.youtube.com/@nanook_nkit"><img src="https://img.shields.io/badge/YouTube-Channel-red?logo=youtube&logoColor=white" alt="YouTube" /></a>
</p>

---

## What is NKit?

NKit is a free, multiplatform disc image processor written in C#/.NET (native AOT compiled). It reads and writes 15+ disc image formats, processes images directly from archives without extraction, and provides scanning, verification, conversion, extraction, fixing, and deduplication — all in one tool.

NKit started nearly 10 years ago as a GameCube/Wii recovery tool. NKit 2 is a full rewrite encompassing all the functionality of dozens of scattered community tools into one unified, cross-platform toolkit.

**Four applications ship together:**

| App | Type | Description |
|-----|------|-------------|
| `nkit` | CLI | Disc image processing (convert, extract, scan, verify, fix, dedupe) |
| `nkit-ui` | GUI | Same features with drag-and-drop, progress bars, visual config |
| `nkds` | CLI | DataStore management (add, mount, export, compact, verify) |
| `nkds-ui` | GUI | Full DataStore UI with storage analysis, per-image stats, mounting |

All binaries are native AOT executables — **no .NET runtime required**. Download, run, done.

---

## NKDS — The NKit DataStore

NKDS is a deduplicated game-storage and archiving format. It understands what's inside disc images — not just raw bytes — and uses that knowledge for massive space savings.

<p align="center">
  <a href="https://youtu.be/s1tzQhFwMS4">
    <img src="https://img.youtube.com/vi/s1tzQhFwMS4/maxresdefault.jpg" alt="NKDS Launch Video" width="600" />
  </a>
  <br/>
  <em>Watch the NKDS introduction video</em>
</p>

### Why NKDS?

Storage costs money. A full Wii set is 16 TB. Compressed to RVZ that's still ~6 TB. **NKDS stores it in under 3 TB** — an 83% reduction.

This isn't better compression. It's **filesystem-level deduplication** — shared system updates, engine libraries, and textures stored once instead of thousands of times, across your entire collection.

### Key Features

- **~80% space savings** — Deduplication + Zstandard compression
- **Mountable virtual drive** — Emulators see regular ISOs, no extraction needed (Dokan/FUSE)
- **Byte-perfect reconstruction** — Every image verifiable, no data loss
- **Crash-safe** — Dual-header atomic commits, automatic recovery from power loss
- **Flexible storage** — Single-file (portable per-game) or multi-file (scalable to hundreds of TB)
- **Any source format** — ISO, RVZ, WBFS, WUX, CHD, archives — NKDS reads them all
- **Multiple workflows** — Use `nkds` / `nkds-ui` directly, or use NKit's dedupe task (`nkit -task dedupe`) to create and populate NKDS sets
- **Folder storage** — Store non-disc content too (MAME sets, versioned ROM collections)
- **Export on demand** — Reconstruct to any format (ISO, RVZ, WUX, CSO...) directly from the store

---

## NKit — Features at a Glance

### Supported Systems

| System | Convert | Extract | Fix | Scan | Verify | Dedupe (NKDS) |
|--------|:-------:|:-------:|:---:|:----:|:------:|:-------------:|
| GameCube | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Wii | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Wii U | ✅ | ✅ | — | ✅ | ✅ | ✅ |
| PS3 | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| PS1 / PS2 / PSP | ✅ | ✅ | — | ✅ | ✅ | ✅ |
| Dreamcast | ✅ | ✅ | — | ✅ | ✅ | ✅ |
| Saturn / Sega CD / CD-i / PC Engine | ✅ | ✅ | — | ✅ | ✅ | ✅ |
| Xbox / Xbox 360 | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Default / ISO9660 | ✅ | ✅ | — | ✅ | ✅ | ✅ |

### Supported Formats

**Reads:** ISO, RVZ, WIA, WBFS, CISO, WUD, WUX, GCZ, CHD, CSO, ZSO, DAX, JSO, CUE+BIN, GDI, NKit (legacy), CDN/APP, XISO

**Writes:** ISO, RVZ (Zstandard/LZMA), WBFS, CISO, WUX, CSO, ZSO, APP, CUE, GDI

### Archive Support (Forward-Reading)

Process images directly from archives — no temp files, no extraction to disk:

- **RAR** (1–5) — split, multi-volume, solid
- **ZIP / ZipX** — split, multi-volume, Zstandard/XZ compression
- **7-Zip** — split, solid, Zstandard
- **GZip** — split

### Processing Tasks

- **Convert** — Between any readable/writable format pair, lossless or lossy
- **Extract** — Pull files from disc images with wildcard/regex masks
- **Scan** — Generate container-independent XML fingerprints
- **Verify** — Multiple fallback methods (checksums, dat matching, scan comparison)
- **Fix** — Repair modified images (unscrub, restore partitions, rebuild from IRD)
- **Dedupe** — Deduplicate to NKDS DataStore

---

## Platform Support

| Platform | Architecture | Notes |
|----------|-------------|-------|
| Windows | x64, ARM64 | No runtime needed. SmartScreen bypass on first run. |
| Linux | x64, ARM64 | `chmod +x` then run. Legacy builds for older glibc. |
| macOS | Apple Silicon, Intel | Remove quarantine attribute on first run. |

---

## Downloads

Releases are published to this repository. Check the [Releases](https://github.com/Nanook/NKit/releases) page for the latest builds.

For documentation and setup instructions, see the [Wiki](https://github.com/Nanook/NKit/wiki).

---

## Source Code

The source is currently in a private repository while a final refactor is completed. **It will be made public soon.** The code has been in active development for nearly 10 years and I want it in a clean state before opening it up.

In the meantime:
- Releases are available and fully functional
- Bugs and feature requests are welcome on [Discord](https://discord.gg/YT792u5yWJ)
- No pull requests yet — the codebase is still moving and I don't want contributors working against a shifting foundation

---

## FAQ

**Q: When will the source code be released?**
Soon. The core is done and stable — I'm cleaning up the codebase so it's presentable and maintainable by others. I'll announce it on Discord.

**Q: Can I contribute?**
Not yet. Once the source is public, contributions will be welcome. For now, testing and feedback on Discord is the most valuable help.

**Q: Is this free?**
Yes, always. NKit and NKDS are a hobby and a gift to the communities I enjoy being part of. If you'd like to support the work, you can [buy me a beer](https://ko-fi.com/nanook_) or [sponsor me](https://ko-fi.com/nanook_/tiers).

**Q: Does NKDS support all systems?**
The DataStore (add/mount/export) currently supports GameCube, Wii, and Wii U. More systems are coming. NKit itself (convert, extract, scan, verify) already supports all 14 systems.

**Q: Do I need to install .NET?**
No. All releases are native AOT binaries — fully self-contained.

**Q: Where do I report bugs?**
On [Discord](https://discord.gg/YT792u5yWJ). Once the source is public, GitHub Issues will be the primary channel.

**Q: What about NKit 1 / the old nkit.iso format?**
NKit 2 reads the legacy nkit.iso/nkit.gcz format but no longer writes it. RVZ replaced it as the recommended compressed format. Use NKit 2 to convert your old nkit files to RVZ.

---

## Links

| | |
|---|---|
| Discord | https://discord.gg/YT792u5yWJ |
| Ko-fi (Donate) | https://ko-fi.com/nanook_ |
| Ko-fi (Sponsor) | https://ko-fi.com/nanook_/tiers |
| YouTube | https://www.youtube.com/@nanook_nkit |
| NKDS Launch Video | https://youtu.be/s1tzQhFwMS4 |

---

## License

License will be announced with the source code release.

---

<p align="center">
  <sub>NKit & NKDS by <a href="https://github.com/Nanook">Nanook</a> — nearly 10 years of late-night development for game preservation.</sub>
</p>
