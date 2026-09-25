using Nanook.NKit.ImageBuilderStreams;
using Nanook.NKit.Nintendo.WiiGc;
using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Linq;

namespace Nanook.NKit
{
    // Compact GameCube ImageBuilder stream that reuses the shared ImageBuilder base.
    // Provides minimal lazy NJunk generation for gaps when JunkID metadata is present.
    public class ImageBuilderGameCubeStream : ImageBuilder
    {
        private readonly IImageReader _imageReader;
        private readonly ImageBuilderNJunkGenerator _junkGenerator;
        private bool _hasJunk;

        public ImageBuilderGameCubeStream(IImageReader reader, int maxCachedBuffers = 0x10, bool disposeReader = true, IBlockProvider blockProvider = null, ImageBufferCache sharedBufferCache = null, OffsetsManagerCacheResult cachedOffsets = null)
            : base(reader, maxCachedBuffers, blockProvider: blockProvider, disposeReader: disposeReader, sharedBufferCache: sharedBufferCache, cachedOffsets: cachedOffsets)
        {
            _imageReader = reader ?? throw new ArgumentNullException(nameof(reader));
            _junkGenerator = new ImageBuilderNJunkGenerator(maxJunkCacheBlocks: 32, maxDiscJunkCacheBlocks: 16);

            try
            {
                // Find first FileSystem area and use its metadata for junk initialization
                AreaRecord fsAreaRec = _imageReader.GetAreas().FirstOrDefault(a => string.Equals(a.Metadata.GetString(AreaValueType.FsType), "FileSystem", StringComparison.OrdinalIgnoreCase));
                if (fsAreaRec != null)
                {
                    string junkId = fsAreaRec.Metadata.GetString(AreaValueType.JunkID);
                    if (!string.IsNullOrEmpty(junkId))
                    {
                        int discNo = (int)(fsAreaRec.Metadata.GetLong(AreaValueType.DiscNo) ?? 0);
                        long startOffset = fsAreaRec.Metadata.GetLong(AreaValueType.JunkLeadingNulls) ?? 0;
                        long total = fsAreaRec.Size;
                        long endRemainder = total % NJunk.JunkBlockSize;

                        _junkGenerator.InitializePartition(junkId, discNo, startOffset, total, 0);
                        _hasJunk = true;
                    }
                }
            }
            catch { /* tolerant */ }
        }

        // Minimal gap fill: if JunkID present generate NJunk into buffer; otherwise fallback to base behavior (zeros)
        protected override void OnGapFill(long offsetInGap, long cleanAreaOffset, long cleanBufferOffset, long size, NKitDataStore.ImageBuilder.BufferContext context)
        {
            if (size <= 0)
                return;

            // Obtain area from context (cleanAreaOffset is provided)
            AreaRecord area = context.Area;

            if (!_hasJunk)
            {
                base.OnGapFill(offsetInGap, cleanAreaOffset, cleanBufferOffset, size, context);
                return;
            }

            // Determine if this area is a filesystem area
            string fsType = area.Metadata.GetString(AreaValueType.FsType);
            bool isFileSystem = string.Equals(fsType, "FileSystem", StringComparison.OrdinalIgnoreCase);

            // Use Wii-style padding behavior: apply alignment+0x1C only when fragment is at start of gap
            try
            {
                long fragStart = cleanAreaOffset;
                int remaining = (int)size;
                int dest = (int)cleanBufferOffset;
                // Delegate to centralized generator
                _junkGenerator.WriteJunkWithPadding(area, ref offsetInGap, cleanAreaOffset, ref fragStart, ref remaining, ref dest, useAlignPadding: true, isFileSystem: isFileSystem, writeToBuffer: (src, srcOff, off, len) => WriteToBuffer(src, srcOff, off, len, context));
            }
            catch
            {
                base.OnGapFill(offsetInGap, cleanAreaOffset, cleanBufferOffset, size, context);
            }
        }

        protected override void Dispose(bool disposing) =>
            // Do not dispose _imageReader here. Readers may be pooled/shared; lifetime
            // is managed by the caller or ImageReaderPool.
            base.Dispose(disposing);
    }
}