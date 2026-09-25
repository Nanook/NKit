using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nanook.NKit
{
    /// <summary>
    /// Stream-based access to Xbox/Xbox360 disc images from the NKit DataStore.
    /// Extends ImageBuilder to provide Xbox-specific gap filling (null bytes or aux filler restoration)
    /// and BlockPadding restoration. Xbox discs do not use data-level encryption — security is
    /// handled at the drive firmware/authentication level.
    /// </summary>
    public class ImageBuilderXboxStream : ImageBuilder
    {
        private readonly IImageReader _imageReader;
        private readonly IBlockProvider _auxBlockProvider;

        private readonly Dictionary<long, XboxAreaContext> _areaContexts = new Dictionary<long, XboxAreaContext>();

        /// <summary>
        /// Pre-computed context for each area containing all properties needed for reconstruction.
        /// </summary>
        private class XboxAreaContext
        {
            public AreaRecord Area { get; set; }
            public bool IsFileSystem { get; set; }
            public bool IsOther { get; set; }
        }

        /// <summary>
        /// Creates a new ImageBuilderXboxStream for reading Xbox disc images from the DataStore.
        /// </summary>
        /// <param name="imageReader">The image reader to read data from.</param>
        /// <param name="blockProvider">Block provider for resolving data blocks (primary + aux resolution).</param>
        /// <param name="auxBlockProvider">Optional aux block provider for restoring filler data from aux store.
        /// When provided, filler regions in the game partition are restored from aux blocks.
        /// When null, filler regions are filled with null bytes (0x00).</param>
        public ImageBuilderXboxStream(IImageReader imageReader, IBlockProvider blockProvider = null, IBlockProvider auxBlockProvider = null)
            : base(imageReader, maxCachedBuffers: 0x10, blockProvider: blockProvider)
        {
            _imageReader = imageReader ?? throw new ArgumentNullException(nameof(imageReader));
            _auxBlockProvider = auxBlockProvider;

            buildAreaContexts();
        }

        /// <summary>
        /// Builds normalized XboxAreaContext for all areas, pre-computing all properties.
        /// </summary>
        private void buildAreaContexts()
        {
            List<AreaRecord> areas = _imageReader.GetAreas().OrderBy(a => a.Offset).ToList();

            foreach (AreaRecord area in areas)
            {
                string fsType = area.Metadata.GetString(AreaValueType.FsType);
                bool isFileSystem = string.Equals(fsType, "FileSystem", StringComparison.OrdinalIgnoreCase);
                bool isOther = string.Equals(fsType, "Other", StringComparison.OrdinalIgnoreCase);

                XboxAreaContext context = new XboxAreaContext
                {
                    Area = area,
                    IsFileSystem = isFileSystem,
                    IsOther = isOther,
                };

                _areaContexts[area.Id] = context;
            }


        }

        /// <summary>
        /// Fills gaps in the reconstructed image.
        /// When auxBlockProvider is available, restores filler data from aux blocks for game partition filler regions.
        /// When auxBlockProvider is null, fills gap regions with null bytes (0x00) for both FileSystem and video partition areas.
        /// </summary>
        protected override void OnGapFill(long offsetInGap, long cleanAreaOffset, long cleanBufferOffset, long size, BufferContext context)
        {
            if (size <= 0)
                return;

            // For Xbox, gap regions are always filled with null bytes (0x00).
            // The aux block provider handles filler restoration through the normal block resolution
            // chain (AuxBlockProvider resolves blocks from primary first, then aux).
            // Gap fill itself always writes zeros — the actual filler data is served as blocks
            // by the AuxBlockProvider when it exists.
            base.OnGapFill(offsetInGap, cleanAreaOffset, cleanBufferOffset, size, context);
        }

        /// <summary>
        /// Called after a buffer has been populated with section data.
        /// Restores BlockPadding records (stored padding bytes) into corresponding buffer positions.
        /// </summary>
        protected override void OnBufferPopulatedWithSection(int validSize, BufferContext context)
        {
            OffsetSegment seg = context.Section?.Segments?.FirstOrDefault(s => s.Source.Type == BlockType.BlockPadding);
            if (seg == null)
                return;

            OffsetRecord src = seg.Source;
            long segImageStart = src.Offset + seg.SourceOffset;
            int segSize = (int)seg.Size;
            if (segSize <= 0)
                return;

            int blkSize = _imageReader.Info.BlockSize;
            int blocksNeeded = (segSize + blkSize - 1) / blkSize;

            // Read the first block to get the padding data
            BlockRecord firstBlock = _imageReader.GetBlock(src.GetBlockAt(0));
            if (firstBlock == null || firstBlock.Data == null || firstBlock.Data.Length == 0)
                return;

            // Determine header skip (first byte indicates header length)
            int headerSkip = 1 + Math.Max(0, (int)firstBlock.Data[0]);
            int payloadLen = Math.Max(0, Math.Min(firstBlock.Data.Length - headerSkip, segSize - headerSkip));
            if (payloadLen <= 0)
                return;

            // Copy padding data into the buffer at the correct position
            long destPos = segImageStart - context.ImageOffset;
            if (destPos < 0 || destPos + payloadLen > context.Buffer.Length)
                return;

            // Read padding bytes from blocks and write into buffer
            int remaining = payloadLen;
            int srcOffset = headerSkip;
            int dstOffset = (int)destPos;

            for (int bi = 0; bi < blocksNeeded && remaining > 0; bi++)
            {
                BlockRecord block = bi == 0 ? firstBlock : _imageReader.GetBlock(src.GetBlockAt(bi));
                if (block == null || block.Data == null)
                    break;

                int offInBlock = bi == 0 ? srcOffset : 0;
                int availableInBlock = block.Data.Length - offInBlock;
                int copyLen = Math.Min(availableInBlock, remaining);
                if (copyLen <= 0)
                    break;

                Array.Copy(block.Data, offInBlock, context.Buffer, dstOffset, copyLen);
                remaining -= copyLen;
                dstOffset += copyLen;
            }
        }

        protected override void Dispose(bool disposing) =>
            // Do not dispose _imageReader here. Readers may be pooled/shared; lifetime
            // is managed by the caller or ImageReaderPool.
            base.Dispose(disposing);
    }
}