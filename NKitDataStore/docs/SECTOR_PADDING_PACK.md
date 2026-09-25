# Sector Padding Pack Format

## Overview

The Sector Padding Pack is a compact, variable-length binary format that stores only the **non-recreatable bytes** of each raw ISO 9660 sector within a section. It is persisted as a single `BlockType.BlockPadding` record per section in the NKit DataStore.

Most Mode1 sectors are fully conforming — their sync pattern, MSF address, mode byte, EDC, and ECC can all be deterministically regenerated from the stored user data and the area's starting LBA. For these sectors, the pack stores nothing. Only sectors with non-standard components (copy protection, intentional errors, Mode2 subheaders, extended user data) consume storage.

This format replaces the legacy `FinaliseSectionAndPersistBlockPadding` approach, which stored fixed-size header blocks for every non-conforming sector and could not handle EDC/ECC mismatches or Mode2Form2 extended user data.

## Binary Layout

```
┌─────────────────────────────────────────────────────────────────┐
│ Offset 0: Version Byte (1 byte)                                 │
│           Value: 0x01 for this format revision                  │
├─────────────────────────────────────────────────────────────────┤
│ Offset 1: Sector Count (2 bytes, unsigned little-endian)        │
│           Number of sectors in the section                      │
├─────────────────────────────────────────────────────────────────┤
│ Offset 3: Presence Bitmap (ceil(sectorCount / 8) bytes)         │
│           bit[i] = 1 → sector i has stored data                 │
│           bit[i] = 0 → sector i is fully conforming (no data)   │
├─────────────────────────────────────────────────────────────────┤
│ Sector Data (variable length):                                  │
│   For each sector where the bitmap bit is SET, in order:        │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │ Flag Byte (1 byte)                                      │   │
│   │   bits 0-1: sector type                                 │   │
│   │   bits 2-7: component presence flags                    │   │
│   ├─────────────────────────────────────────────────────────┤   │
│   │ Payload (variable length, in bit order):                │   │
│   │   [sync 12B][msf+mode 4B][subhdr 8B][edc 4B]           │   │
│   │   [ecc 276B][ext_userdata 280B]                         │   │
│   └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

The **Pack_Header** consists of the version byte, sector count, and presence bitmap. The remaining bytes are the per-sector flag bytes and payloads, ordered sequentially for each sector whose bitmap bit is set.

## Version Byte and Forward Compatibility

The first byte of every Sector Padding Pack is a format version number:

| Version | Meaning |
|---------|---------|
| `0x01`  | Current format (described in this document) |
| Other   | Unsupported — the Unpacker throws `InvalidDataException` |

Future format revisions increment this byte. Readers that encounter an unrecognized version must reject the pack rather than attempting to parse it. This ensures forward compatibility without silent data corruption.

## Flag Byte Bit Assignments

The Flag Byte encodes both the sector type and which non-recreatable components are stored:

```
  Bit:  7       6       5       4       3       2       1   0
      ┌───────┬───────┬───────┬───────┬───────┬───────┬───────┐
      │ExtData│  ECC  │  EDC  │SubHdr │MSF+Mod│ Sync  │ Type  │
      └───────┴───────┴───────┴───────┴───────┴───────┴───────┘
```

### Sector Type (bits 0-1)

| Value | Type | Description |
|-------|------|-------------|
| `00`  | Mode1 | Standard data sector |
| `01`  | Mode2Form1 | Mode2 with error correction |
| `10`  | Mode2Form2 | Mode2 without error correction |
| `11`  | Audio | Audio sector within a data area |

### Component Flags (bits 2-7)

| Bit | Flag | Payload Size | Description |
|-----|------|-------------|-------------|
| 2 | Sync | 12 bytes | Non-standard sync pattern |
| 3 | MsfMode | 4 bytes | Non-standard MSF (3 bytes) + mode (1 byte) |
| 4 | Subheader | 8 bytes | Mode2 subheader (file, channel, submode, coding ×2) |
| 5 | EDC | 4 bytes | Non-standard Error Detection Code |
| 6 | ECC | 276 bytes | Non-standard Error Correction Code |
| 7 | ExtUserData | 280 bytes | Mode2Form2 extended user data beyond 2048-byte stride |

Payload bytes are written in **bit order** (lowest bit first): sync → MSF/mode → subheader → EDC → ECC → extended user data.

The total payload length for a sector is deterministic from its flag byte alone:

```
payload_length = (bit2 ? 12 : 0) + (bit3 ? 4 : 0) + (bit4 ? 8 : 0)
               + (bit5 ? 4 : 0) + (bit6 ? 276 : 0) + (bit7 ? 280 : 0)
```

## Sector Type Layouts

### Mode1 Sector (2352 bytes)

```
Offset  Size  Component
──────  ────  ─────────────────────────────────
0x000    12   Sync Pattern
0x00C     3   MSF (Minute/Second/Frame, BCD)
0x00F     1   Mode (0x01)
0x010  2048   User Data
0x810     4   EDC (CRC32)
0x814     8   Zero Padding
0x81C   276   ECC (Reed-Solomon P + Q parity)
```

**Packer behavior**: Checks sync, MSF/mode, EDC, and ECC against computed values. A fully conforming Mode1 sector has its bitmap bit cleared (zero storage).

### Mode2Form1 Sector (2352 bytes)

```
Offset  Size  Component
──────  ────  ─────────────────────────────────
0x000    12   Sync Pattern
0x00C     3   MSF (Minute/Second/Frame, BCD)
0x00F     1   Mode (0x02)
0x010     8   Subheader (always stored)
0x018  2048   User Data
0x818     4   EDC (CRC32)
0x81C   276   ECC (Reed-Solomon P + Q parity)
```

**Packer behavior**: Always sets bit 4 (subheader is never recreatable). Checks sync, MSF/mode, EDC, and ECC. Minimum storage per sector: 1 flag byte + 8 subheader bytes = 9 bytes.

### Mode2Form2 Sector (2352 bytes)

```
Offset  Size  Component
──────  ────  ─────────────────────────────────
0x000    12   Sync Pattern
0x00C     3   MSF (Minute/Second/Frame, BCD)
0x00F     1   Mode (0x02)
0x010     8   Subheader (always stored)
0x018  2048   User Data (stored in stride)
0x818   280   Extended User Data (always stored)
0x92C     4   EDC (CRC32)
```

**Packer behavior**: Always sets bit 4 (subheader) and bit 7 (extended user data, since the stride only stores 2048 bytes). Checks sync, MSF/mode, and EDC. No ECC for Mode2Form2. Minimum storage per sector: 1 flag byte + 8 subheader + 280 extended = 289 bytes.

### Audio Sector

Audio sectors within a data area have no internal structure (no sync, MSF, EDC, or ECC). The Packer sets bits 0-1 to `11` and stores no payload. In practice, audio section types are skipped entirely — no pack is produced for audio sections.

## Unconditional Storage Rules

Certain components are **always** stored regardless of conformance:

1. **Mode2 Subheader (bit 4)**: The 8-byte subheader contains file number, channel, submode, and coding information that cannot be regenerated from user data or position alone. Always stored for Mode2Form1 and Mode2Form2 sectors.

2. **Mode2Form2 Extended User Data (bit 7)**: The DataStore stride stores only 2048 bytes of user data, but Mode2Form2 sectors contain 2328 bytes of user data. The additional 280 bytes (offsets 0x818–0x92B) must always be stored.

## Integration with DataStore BlockPadding Records

The Sector Padding Pack is stored as a single `BlockType.BlockPadding` record per section:

```
DataStoreIso9660Formatter
    └── FinaliseSectionAndPersistBlockPadding()
            ├── Calls SectorPaddingPacker.Pack(sectionData, sectorCount, startLba)
            ├── Writes result as BlockPadding record via IImageWriter.WriteData
            └── One record per section (even if all sectors are conforming)
```

**Key invariant**: Every section produces exactly one BlockPadding record. When all sectors are conforming, the record contains only the version byte and Pack_Header (sector count + empty bitmap) with zero payload bytes. This maintains a consistent one-record-per-section mapping.

**Reconstruction flow**:

```
ImageBuilderIso9660Stream
    └── OnBufferPopulatedWithSection()
            ├── Reads BlockPadding record for the section
            ├── Calls SectorPaddingUnpacker.Unpack(packData, buffer, offset, sectorCount)
            └── Overlay is applied AFTER ReconstructPrefix and BEFORE/instead of
                EDC/ECC regeneration for sectors with non-standard EDC/ECC
```

## PhysicalOffset and Deterministic MSF Computation

Each data area in the DataStore stores a `PhysicalOffset` value in its area metadata. This is the starting LBA (Logical Block Address) of the first sector in the area.

**How it works**:

1. During ingestion, the area's starting LBA is recorded as `PhysicalOffset`.
2. During reconstruction, for sector at index `i` within the area, the expected LBA is:
   ```
   expectedLBA = PhysicalOffset + i
   ```
3. The MSF address is computed from the LBA using the standard formula:
   ```
   frame  = (LBA + 150) % 75
   second = ((LBA + 150) / 75) % 60
   minute = (LBA + 150) / (75 * 60)
   ```
   (Values are BCD-encoded in the sector header.)

4. Because the LBA is deterministic from position, most sectors do **not** need their MSF stored — the Packer only sets bit 3 when the actual MSF/mode bytes differ from the computed values.

**When MSF is stored (bit 3 set)**:
- Pregap sectors with intentional MSF offsets
- Lead-in sectors
- Copy-protected sectors with deliberately incorrect addresses
- Any sector where the mode byte differs from expected

## Size Examples

| Scenario | Pack Size | Breakdown |
|----------|-----------|-----------|
| 16 conforming Mode1 sectors | 5 bytes | 1 (ver) + 2 (count) + 2 (bitmap) |
| 16 Mode2Form1 sectors (conforming except subheader) | 149 bytes | 5 (header) + 16 × (1 flag + 8 subheader) |
| 1 sector with non-standard sync + MSF | 21 bytes | 1 + 2 + 1 (bitmap) + 1 (flag) + 12 + 4 |
| 1 Mode2Form2 sector (conforming sync/MSF/EDC) | 293 bytes | 1 + 2 + 1 + 1 (flag) + 8 (sub) + 280 (ext) |
| 1 Mode1 sector with bad EDC + ECC (copy protection) | 285 bytes | 1 + 2 + 1 + 1 (flag) + 4 (edc) + 276 (ecc) |

For comparison, the legacy format stored 16 bytes per non-conforming Mode1 sector and 24 bytes per Mode2 sector, regardless of which specific components were non-standard.

## Non-Conformance Detection

The Packer detects non-recreatable data by comparing actual sector bytes against values computed by the ECM helper functions:

| Component | Detection Method | Flag Bit |
|-----------|-----------------|----------|
| Sync | Compare bytes 0x000–0x00B against standard sync pattern | 2 |
| MSF + Mode | Compare bytes 0x00C–0x00F against `Ecm.ReconstructPrefix` output | 3 |
| EDC | Compare 4 bytes at type-specific offset against computed CRC32 | 5 |
| ECC | Compare 276 bytes at type-specific offset against `Ecm.ReconstructEcc` output | 6 |

The sector type is determined exclusively from the raw sector bytes (mode byte at offset 0x0F and subheader submode byte), not from external metadata such as cue sheets or GDI track descriptors.

## Error Handling

| Condition | Behavior |
|-----------|----------|
| Unsupported version byte | `InvalidDataException` thrown with version identifier |
| Pack data truncated | `InvalidDataException` thrown with byte position |
| Sector count mismatch | Warning logged, processes min(pack count, actual count) |
| IImageWriter.WriteData failure | Error logged, exception propagated to caller |
| Null/empty section data | Early return, no pack produced |
| Block size ≠ 0x930 | Early return, no pack needed for non-raw sectors |
| Audio section type | Early return, audio has no sector structure |
