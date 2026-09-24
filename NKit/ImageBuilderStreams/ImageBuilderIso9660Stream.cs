using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Nanook.NKit
{
    /// <summary>
    /// Stream-based access to ISO9660 disc images with sector header recreation and EDC/ECC regeneration.
    /// Extends NKitDataStore.ImageBuilder to provide ISO9660-specific reconstruction logic for
    /// Mode1, Mode2Form1, Mode2Form2, and Audio tracks. Supports multi-track area transitions
    /// and optional PS3 AES-128-CBC encryption.
    /// </summary>
    public class ImageBuilderIso9660Stream : ImageBuilder
    {
        private readonly IImageReader _imageReader;

        // Current area state — updated in OnAreaChanged when crossing track boundaries
        private int _currentBlockSize;
        private long _currentPhysicalOffset; // base LBA for the current area
        private string _currentTrackType;    // "Mode1", "Mode2", "Audio", etc.
        private bool _currentIsEncrypted;
        private byte[] _currentTitleKey;

        // Pre-computed context for all areas (indexed by area ID)
        private readonly Dictionary<long, Iso9660AreaContext> _areaContexts = new Dictionary<long, Iso9660AreaContext>();

        /// <summary>
        /// Normalized context for each area containing pre-computed properties.
        /// </summary>
        private class Iso9660AreaContext
        {
            public AreaRecord Area { get; set; }
            public int BlockSize { get; set; }
            public long PhysicalOffset { get; set; } // base LBA
            public string TrackType { get; set; }    // "Mode1", "Mode2", "Audio"
            public bool IsEncrypted { get; set; }
            public byte[] TitleKey { get; set; }
        }

        /// <summary>
        /// Creates a new ImageBuilderIso9660Stream for reading ISO9660 disc images.
        /// </summary>
        /// <param name="reader">The image reader to read data from.</param>
        /// <param name="maxCachedBuffers">Maximum number of buffers to cache (default: 16).</param>
        /// <param name="disposeReader">Whether to dispose the reader when this stream is disposed.</param>
        /// <param name="blockProvider">Optional block provider for data access.</param>
        /// <param name="sharedBufferCache">Optional shared buffer cache.</param>
        /// <param name="cachedOffsets">Optional cached offsets.</param>
        public ImageBuilderIso9660Stream(IImageReader reader, int maxCachedBuffers = 0x10, bool disposeReader = true, IBlockProvider blockProvider = null, ImageBufferCache sharedBufferCache = null, OffsetsManagerCacheResult cachedOffsets = null)
            : base(reader, maxCachedBuffers, blockProvider: blockProvider, disposeReader: disposeReader, sharedBufferCache: sharedBufferCache, cachedOffsets: cachedOffsets)
        {
            _imageReader = reader ?? throw new ArgumentNullException(nameof(reader));

            // Pre-parse all areas and build normalized context
            buildAreaContexts();
        }

        /// <summary>
        /// Builds normalized Iso9660AreaContext for all areas, pre-computing all properties.
        /// </summary>
        private void buildAreaContexts()
        {
            List<AreaRecord> areas = _imageReader.GetAreas().OrderBy(a => a.Offset).ToList();

            foreach (AreaRecord area in areas)
            {
                int blockSize = (int)(area.Metadata.GetLong(AreaValueType.BlockSize) ?? 0x800);
                long physicalOffset = area.Metadata.GetLong(AreaValueType.PhysicalOffset) ?? 0;
                string trackType = area.Metadata.GetString(AreaValueType.Type) ?? "";
                bool isEncrypted = area.Metadata.GetBool(AreaValueType.Encrypted) ?? false;

                string titleKeyStr = area.Metadata.GetString(AreaValueType.TitleKey);
                string threeKeyStr = area.Metadata.GetString(AreaValueType.ThreeKey);

                byte[] titleKey = null;
                if (isEncrypted)
                {
                    if (!string.IsNullOrEmpty(titleKeyStr))
                    {
                        try { titleKey = titleKeyStr.HexToBytes(); }
                        catch { /* tolerant */ }
                    }

                    // Also try ThreeKey as fallback
                    if (titleKey == null || titleKey.Length == 0)
                    {
                        if (!string.IsNullOrEmpty(threeKeyStr))
                        {
                            try { titleKey = threeKeyStr.HexToBytes(); }
                            catch { /* tolerant */ }
                        }
                    }

                }

                Iso9660AreaContext context = new Iso9660AreaContext
                {
                    Area = area,
                    BlockSize = blockSize,
                    PhysicalOffset = physicalOffset,
                    TrackType = trackType,
                    IsEncrypted = isEncrypted,
                    TitleKey = titleKey
                };

                _areaContexts[area.Id] = context;

                // Propagate the first discovered key to the base class Key property
                // so DataStoreAsIso can pass it to _context.SourceFile.Key for downstream steps
                if (titleKey != null && titleKey.Length > 0 && Key == null)
                    Key = titleKey;
            }

            // Initialize current state from first area if available
            if (_areaContexts.Count > 0)
            {
                Iso9660AreaContext first = _areaContexts.Values.First();
                _currentBlockSize = first.BlockSize;
                _currentPhysicalOffset = first.PhysicalOffset;
                _currentTrackType = first.TrackType;
                _currentIsEncrypted = first.IsEncrypted;
                _currentTitleKey = first.TitleKey;
            }
        }

        /// <summary>
        /// Called when the stream position moves into a different area.
        /// Updates current block size, stride configuration, and base LBA from the new area's metadata.
        /// </summary>
        protected override void OnAreaChanged(AreaRecord newArea, AreaRecord oldArea)
        {
            base.OnAreaChanged(newArea, oldArea);

            if (_areaContexts.TryGetValue(newArea.Id, out Iso9660AreaContext context))
            {
                _currentBlockSize = context.BlockSize;
                _currentPhysicalOffset = context.PhysicalOffset;
                _currentTrackType = context.TrackType;
                _currentIsEncrypted = context.IsEncrypted;
                _currentTitleKey = context.TitleKey;
            }
        }

        /// <summary>
        /// Called when a buffer has been fully populated.
        /// Iterates over each sector in the buffer and regenerates headers/ECC based on sector mode.
        /// For raw sectors (blockSize 0x930), regenerates sync, MSF, mode byte, EDC, and ECC.
        /// Uses a two-phase pack overlay:
        ///   Phase 1 (pre-ECC): Apply subheader + extended user data so EDC/ECC computation is correct
        ///   Phase 2 (post-ECC): Apply non-standard sync, MSF, EDC, ECC overlays
        /// Audio tracks pass through without modification.
        /// </summary>
        protected override void OnBufferComplete(int validSize, BufferContext context)
        {
            base.OnBufferComplete(validSize, context);

            if (!_areaContexts.TryGetValue(context.Area.Id, out Iso9660AreaContext areaContext))
                return;

            int blockSize = areaContext.BlockSize;

            // Audio tracks: pass through without modification
            if (string.Equals(areaContext.TrackType, "Audio", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            byte[] buffer = context.Buffer;

            // Raw sectors (0x930): regenerate sector headers and EDC/ECC
            if (blockSize == 0x930)
            {
                long areaOffset = context.ImageOffset - context.Area.Offset;

                int sectorCount = validSize / 0x930;

                // Get pack data if available
                context.Items.TryGetValue("SectorPaddingPack", out object packObj);
                byte[] packData = packObj as byte[];

                // PHASE 1: Apply subheader + extended user data from pack BEFORE ReconstructEcc.
                // This is critical because EDC/ECC computation for Mode2 sectors includes the
                // subheader bytes (offset 0x10) and extended user data (offset 0x818 for Form2).
                // Without this, ReconstructEcc would compute EDC over zeros instead of actual subheader.
                SectorFlags?[] sectorTypes = null;
                if (packData != null)
                {
                    SectorPaddingUnpacker.UnpackPreEcc(packData, buffer, 0, sectorCount, out sectorTypes);
                }

                // Regenerate sync, MSF, mode, EDC, and ECC for each sector
                for (int i = 0; i < sectorCount; i++)
                {
                    int sectorOffset = i * 0x930;

                    // Compute sector LBA: areaPhysicalOffset + (bufferPositionWithinArea / blockSize)
                    long sectorLba = areaContext.PhysicalOffset + ((areaOffset + sectorOffset) / 0x930);

                    // Determine sector mode. If we have pack data with sector type info, use that
                    // (more reliable since the buffer may not have subheader bytes for Form detection).
                    // Otherwise fall back to buffer inspection.
                    bool isMode1;
                    bool isMode2Form1 = false;
                    bool isMode2Form2 = false;

                    if (sectorTypes != null && sectorTypes[i].HasValue)
                    {
                        // Use sector type from pack flags (authoritative)
                        SectorFlags type = sectorTypes[i].Value & SectorFlags.TypeMask;
                        isMode1 = type == SectorFlags.Mode1;
                        isMode2Form1 = type == SectorFlags.Mode2Form1;
                        isMode2Form2 = type == SectorFlags.Mode2Form2;
                    }
                    else if (string.Equals(areaContext.TrackType, "Mode1", StringComparison.OrdinalIgnoreCase))
                    {
                        isMode1 = true;
                    }
                    else
                    {
                        // For Mode2 sectors without pack info, check the subheader
                        // (after pre-ECC phase, subheader should be populated if it was in the pack)
                        isMode1 = false;
                        byte subMode = buffer[sectorOffset + 0x12];
                        isMode2Form2 = (subMode & 0x20) != 0;
                        isMode2Form1 = !isMode2Form2;
                    }

                    // Regenerate sync pattern and MSF header
                    Ecm.ReconstructPrefix(buffer, sectorOffset, isMode1, sectorLba);

                    // Regenerate EDC and ECC (now with correct subheader/ext data in buffer)
                    if (isMode1 || isMode2Form1)
                    {
                        Ecm.ReconstructEcc(buffer, sectorOffset, isMode1, isMode2Form1, isMode2Form2);
                    }
                    else if (isMode2Form2)
                    {
                        Ecm.ReconstructEcc(buffer, sectorOffset, false, false, true);
                    }
                }

                // PHASE 2: Apply non-standard sync, MSF, EDC, ECC from pack AFTER ReconstructEcc.
                // This overwrites the computed values with the actual non-standard bytes for
                // sectors with copy protection or intentional errors.
                if (packData != null)
                {
                    SectorPaddingUnpacker.UnpackPostEcc(packData, buffer, 0, sectorCount);
                }

            }
        }

        /// <summary>
        /// Applies PS3 AES-128-CBC encryption to the buffer in 0x800-byte sectors.
        /// PS3 disc encryption operates on the full raw data in 2048-byte chunks,
        /// with IV derived from (absoluteImageOffset + chunkOffset) / 0x800.
        /// This matches the PlayStation3.encode() method used during scan.
        /// </summary>
        private void applyPs3Encryption(byte[] buffer, int validSize, long imageOffset, byte[] titleKey)
        {
            const int secSize = 0x800; // PS3 encryption sector size (2048 bytes)

            int chunkCount = validSize / secSize;
            if (chunkCount <= 0)
                return;

            using (Aes aes = Aes.Create())
            {
                aes.Padding = PaddingMode.None;
                aes.Mode = CipherMode.CBC;
                aes.Key = titleKey;
                byte[] iv = new byte[16];

                for (int i = 0; i < chunkCount; i++)
                {
                    int off = i * secSize;

                    // IV derivation matches PlayStation3.encode():
                    // iv = (imageOffset + off) / _secSize
                    iv.WriteUInt64B(8, (ulong)(imageOffset + off) / (ulong)secSize);
                    aes.IV = iv;

                    using (ICryptoTransform encryptor = aes.CreateEncryptor())
                    {
                        encryptor.TransformBlock(buffer, off, secSize, buffer, off);
                    }
                }
            }
        }

        /// <summary>
        /// Called after a buffer has been populated with section data.
        /// Reads the BlockPadding record (Sector_Padding_Pack) for the section and stores
        /// the pack data in context.Tag for use by OnBufferComplete. The unpacker overlay
        /// is applied after ReconstructPrefix and ReconstructEcc in OnBufferComplete.
        /// </summary>
        protected override void OnBufferPopulatedWithSection(int validSize, BufferContext context)
        {
            OffsetSegment seg = context.Section?.Segments?.FirstOrDefault(s => s.Source.Type == BlockType.BlockPadding);

            if (seg == null)
                return;

            OffsetRecord src = seg.Source;

            int segSize = (int)seg.Size;
            if (segSize <= 0)
                return;


            int blkSize = _imageReader.Info.BlockSize;
            int blocksNeeded = (segSize + blkSize - 1) / blkSize;

            // Read all blocks and assemble the complete pack data
            byte[] packData = new byte[segSize];
            int totalCopied = 0;

            for (int bi = 0; bi < blocksNeeded; bi++)
            {
                BlockKey key = src.GetBlockAt(bi);
                BlockRecord blockRecord = _imageReader.GetBlock(key);

                if (blockRecord == null || blockRecord.Data == null)
                    break;

                int availableInSeg = Math.Min(blockRecord.Data.Length, segSize - (bi * blkSize));
                if (availableInSeg <= 0)
                    break;

                Array.Copy(blockRecord.Data, 0, packData, bi * blkSize, availableInSeg);
                totalCopied += availableInSeg;
            }

            if (totalCopied == 0)
                return;

            // If we didn't fill the full expected size, trim to what we got
            if (totalCopied < segSize)
            {
                byte[] trimmed = new byte[totalCopied];
                Array.Copy(packData, 0, trimmed, 0, totalCopied);
                packData = trimmed;
            }

            // Store pack data in context.Items for OnBufferComplete to apply after regeneration
            context.Items["SectorPaddingPack"] = packData;
        }

        /// <summary>
        /// Fills gaps in the reconstructed image with null bytes (0x00) including
        /// both the sector header region and the data region of each gap sector.
        /// </summary>
        protected override void OnGapFill(long offsetInGap, long cleanAreaOffset, long cleanBufferOffset, long size, BufferContext context)
        {
            if (size <= 0)
                return;

            // Fill gap regions with null bytes (0x00) — the base implementation already does this
            base.OnGapFill(offsetInGap, cleanAreaOffset, cleanBufferOffset, size, context);
        }

        /// <summary>
        /// Creates a read-only seekable stream for a multi-extent file, reading each extent
        /// sequentially from its recorded ImageOffset. Seamlessly transitions between extents
        /// without gaps or overlaps in the logical file stream.
        /// For single-extent files (splitParts is null or has &lt; 2 parts), falls back to a
        /// standard BoundedStream at the file's FsOffset.
        /// </summary>
        /// <param name="file">The file to create a stream for.</param>
        /// <returns>A stream presenting the file as a single contiguous byte sequence.</returns>
        public Stream CreateFileStream(IFsFile file)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));

            if (file.FsSize == 0)
                return new MemoryStream(Array.Empty<byte>(), false);

            IFsFileParts splitParts = file.SplitParts;
            if (splitParts != null && splitParts.Parts.Count >= 2)
            {
                return new MultiExtentImageBuilderStream(this, splitParts.Parts);
            }

            // Single-extent: standard bounded read from the file's offset
            return new BoundedImageBuilderStream(this, file.FsOffset, file.FsSize);
        }

        /// <summary>
        /// A read-only stream that reads a multi-extent file from this ImageBuilder by seeking
        /// to each extent's image offset in sequence. Handles reads spanning extent boundaries.
        /// </summary>
        private class MultiExtentImageBuilderStream : Stream
        {
            private readonly ImageBuilderIso9660Stream _builder;
            private readonly List<(long imageOffset, long size)> _extents;
            private readonly long _totalSize;
            private long _position;
            private bool _disposed;

            internal MultiExtentImageBuilderStream(ImageBuilderIso9660Stream builder, IReadOnlyList<IFsFilePart> parts)
            {
                _builder = builder;
                _extents = new List<(long imageOffset, long size)>(parts.Count);
                long total = 0;
                foreach (IFsFilePart part in parts)
                {
                    _extents.Add((part.FsFile.FsOffset, part.FsFile.FsSize));
                    total += part.FsFile.FsSize;
                }
                _totalSize = total;
                _position = 0;
            }

            public override bool CanRead => !_disposed;
            public override bool CanSeek => !_disposed;
            public override bool CanWrite => false;
            public override long Length => _totalSize;

            public override long Position
            {
                get => _position;
                set
                {
                    if (value < 0 || value > _totalSize)
                        throw new ArgumentOutOfRangeException(nameof(value));
                    _position = value;
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(MultiExtentImageBuilderStream));
                if (buffer == null) throw new ArgumentNullException(nameof(buffer));
                if (offset < 0 || offset > buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
                if (count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));

                if (_position >= _totalSize)
                    return 0;

                int totalRead = 0;
                int remaining = (int)Math.Min(count, _totalSize - _position);

                while (remaining > 0)
                {
                    // Find which extent contains the current logical position
                    long cumulative = 0;
                    int extentIdx = -1;
                    long posInExtent = 0;

                    for (int i = 0; i < _extents.Count; i++)
                    {
                        if (_position < cumulative + _extents[i].size)
                        {
                            extentIdx = i;
                            posInExtent = _position - cumulative;
                            break;
                        }
                        cumulative += _extents[i].size;
                    }

                    if (extentIdx < 0)
                        break;

                    (long imageOffset, long size) extent = _extents[extentIdx];
                    long availableInExtent = extent.size - posInExtent;
                    int toRead = (int)Math.Min(remaining, availableInExtent);

                    // Seek the builder to this extent's image offset + position within extent
                    _builder.Position = extent.imageOffset + posInExtent;
                    int bytesRead = _builder.Read(buffer, offset + totalRead, toRead);

                    totalRead += bytesRead;
                    remaining -= bytesRead;
                    _position += bytesRead;

                    if (bytesRead < toRead)
                        break; // Short read from builder
                }

                return totalRead;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(MultiExtentImageBuilderStream));
                long newPos = origin switch
                {
                    SeekOrigin.Begin => offset,
                    SeekOrigin.Current => _position + offset,
                    SeekOrigin.End => _totalSize + offset,
                    _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
                };
                if (newPos < 0 || newPos > _totalSize)
                    throw new ArgumentOutOfRangeException(nameof(offset));
                _position = newPos;
                return _position;
            }

            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    // Do not dispose _builder — it is owned by the caller
                }
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// A simple bounded stream wrapper for single-extent file reads from this ImageBuilder.
        /// </summary>
        private class BoundedImageBuilderStream : Stream
        {
            private readonly ImageBuilderIso9660Stream _builder;
            private readonly long _start;
            private readonly long _length;
            private long _position;
            private bool _disposed;

            internal BoundedImageBuilderStream(ImageBuilderIso9660Stream builder, long start, long length)
            {
                _builder = builder;
                _start = start;
                _length = length;
                _position = 0;
            }

            public override bool CanRead => !_disposed;
            public override bool CanSeek => !_disposed;
            public override bool CanWrite => false;
            public override long Length => _length;

            public override long Position
            {
                get => _position;
                set
                {
                    if (value < 0 || value > _length) throw new ArgumentOutOfRangeException(nameof(value));
                    _position = value;
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(BoundedImageBuilderStream));
                if (buffer == null) throw new ArgumentNullException(nameof(buffer));
                if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException();
                if (_position >= _length) return 0;

                int toRead = (int)Math.Min(count, _length - _position);
                _builder.Position = _start + _position;
                int read = _builder.Read(buffer, offset, toRead);
                _position += read;
                return read;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(BoundedImageBuilderStream));
                long newPos = origin switch
                {
                    SeekOrigin.Begin => offset,
                    SeekOrigin.Current => _position + offset,
                    SeekOrigin.End => _length + offset,
                    _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
                };
                if (newPos < 0 || newPos > _length) throw new ArgumentOutOfRangeException(nameof(offset));
                _position = newPos;
                return _position;
            }

            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    // Do not dispose _builder — it is owned by the caller
                }
                base.Dispose(disposing);
            }
        }

        protected override void Dispose(bool disposing) =>
            // Do not dispose _imageReader here. Readers may be pooled/shared; lifetime
            // is managed by the caller or ImageReaderPool.
            base.Dispose(disposing);
    }
}