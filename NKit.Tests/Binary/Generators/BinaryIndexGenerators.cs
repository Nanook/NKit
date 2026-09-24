using FsCheck;
using FsCheck.Fluent;
using NKitDataStore;
using NKitDataStore.Binary;
using System.Linq;

namespace NKit.Tests.Binary.Generators
{
    /// <summary>
    /// FsCheck generators for binary index format types.
    /// Produces valid instances suitable for serialization round-trip testing.
    /// </summary>
    internal static class BinaryIndexGenerators
    {
        /// <summary>
        /// Generates valid BaseOffset values for embedded mode testing.
        /// Range: 0 to 100000, representing the byte offset where the index starts
        /// within an embedded file (keeps temp files reasonably sized).
        /// </summary>
        internal static Gen<long> ValidBaseOffset()
        {
            return Gen.OneOf(
                Gen.Constant(0L),
                Gen.Choose(1, 100).Select(x => (long)x),
                Gen.Choose(100, 10000).Select(x => (long)x),
                Gen.Choose(10000, 100000).Select(x => (long)x)
            );
        }

        /// <summary>
        /// Generates valid IndexSize values for EmbeddedFooter testing.
        /// Range: 0 to int64.MaxValue, with bias toward boundary values.
        /// </summary>
        internal static Gen<long> ValidIndexSize()
        {
            return Gen.OneOf(
                Gen.Constant(0L),
                Gen.Constant(long.MaxValue),
                Gen.Choose(1, int.MaxValue).Select(x => (long)x),
                Gen.Choose(int.MinValue, int.MaxValue).SelectMany(hi =>
                    Gen.Choose(int.MinValue, int.MaxValue).Select(lo =>
                        ((long)(uint)hi << 32) | (uint)lo)).Where(v => v >= 0)
            );
        }

        /// <summary>
        /// Generates a valid FileHeader with correct magic, supported version,
        /// and structurally valid field values.
        /// </summary>
        internal static Gen<FileHeader> ValidFileHeader()
        {
            return Gen.Choose(8192, int.MaxValue / 2).Select(x => (long)x)
                .SelectMany(imageDirectoryOffset =>
                Gen.Choose(1, 10_000_000).SelectMany(imageDirectorySize =>
                Gen.Choose(8192, int.MaxValue / 2).Select(x => (long)x).SelectMany(blockIndexOffset =>
                Gen.Choose(1, 10_000_000).SelectMany(blockIndexSize =>
                Gen.Choose(0, 100).SelectMany(blockIndexDeltaCount =>
                Gen.Choose(0, int.MaxValue / 2).Select(x => (long)x).SelectMany(blockIndexDeltaHeadOffset =>
                Gen.Choose(8192, int.MaxValue / 2).Select(x => (long)x).SelectMany(fileEndOffset =>
                Gen.OneOf(Gen.Constant(0L), Gen.Choose(1, int.MaxValue / 2).Select(x => (long)x)).SelectMany(shardSize =>
                Gen.Choose(1, 0x100000).SelectMany(blockSize =>
                Gen.Choose(1, 1000).SelectMany(maxOffsetBlocks =>
                Gen.Choose(0, 100_000).Select(imageCount =>
                    new FileHeader
                    {
                        Magic = FileHeader.MagicBytes,
                        MajorVersion = FileHeader.CurrentMajorVersion,
                        MinorVersion = FileHeader.CurrentMinorVersion,
                        ImageDirectoryOffset = imageDirectoryOffset,
                        ImageDirectorySize = imageDirectorySize,
                        BlockIndexOffset = blockIndexOffset,
                        BlockIndexSize = blockIndexSize,
                        BlockIndexDeltaCount = blockIndexDeltaCount,
                        BlockIndexDeltaHeadOffset = blockIndexDeltaHeadOffset,
                        FileEndOffset = fileEndOffset,
                        ShardSize = shardSize,
                        BlockSize = blockSize,
                        MaxOffsetBlocks = maxOffsetBlocks,
                        ImageCount = imageCount,
                    }
                )))))))))));
        }

        /// <summary>
        /// Generates a random BlockKey with arbitrary hash values.
        /// </summary>
        internal static Gen<BlockKey> AnyBlockKey()
        {
            return Gen.Choose(int.MinValue, int.MaxValue).SelectMany(a =>
                Gen.Choose(int.MinValue, int.MaxValue).SelectMany(b =>
                Gen.Choose(int.MinValue, int.MaxValue).Select(c =>
                    new BlockKey(
                        ((ulong)(uint)a << 32) | (uint)b,
                        (uint)c)
                )));
        }

        /// <summary>
        /// Generates a valid AreaRecord with reasonable field values.
        /// </summary>
        internal static Gen<AreaRecord> ValidAreaRecord()
        {
            return Gen.Choose(0, int.MaxValue / 2).Select(x => (long)x).SelectMany(offset =>
                Gen.Choose(1, int.MaxValue / 2).Select(x => (long)x).SelectMany(size =>
                Gen.Choose(0, 0x10000).SelectMany(strideBlockSize =>
                Gen.Choose(0, 0x1000).SelectMany(strideDataOffset =>
                Gen.Choose(0, 0x10000).SelectMany(strideDataLength =>
                Gen.Choose(0, 0x200000).SelectMany(sectionSize =>
                Gen.Choose(int.MinValue, int.MaxValue).SelectMany(crc32Raw =>
                Gen.Choose(int.MinValue, int.MaxValue).SelectMany(xxHash64A =>
                Gen.Choose(int.MinValue, int.MaxValue).Select(xxHash64B =>
                    new AreaRecord
                    {
                        Offset = offset,
                        Size = size,
                        StrideBlockSize = strideBlockSize,
                        StrideDataOffset = strideDataOffset,
                        StrideDataLength = strideDataLength,
                        SectionSize = sectionSize,
                        Crc32 = (uint)crc32Raw,
                        XxHash64 = ((ulong)(uint)xxHash64A << 32) | (uint)xxHash64B,
                        Metadata = new AreaMetadata(),
                    }
                )))))))));
        }

        /// <summary>
        /// Generates a valid FileRecord with reasonable field values.
        /// Names are limited to printable ASCII for simplicity in round-trip tests.
        /// </summary>
        internal static Gen<FileRecord> ValidFileRecord()
        {
            return Gen.Choose(1, 64).SelectMany(nameLength =>
                Gen.ArrayOf(Gen.Choose(0x20, 0x7E).Select(c => (char)c), nameLength).SelectMany(nameChars =>
                Gen.Choose(0, 1000).SelectMany(fileId =>
                Gen.Choose(0, int.MaxValue / 2).Select(x => (long)x).SelectMany(offset =>
                Gen.Choose(1, int.MaxValue / 2).Select(x => (long)x).SelectMany(size =>
                Gen.Choose(1, int.MaxValue / 2).Select(x => (long)x).SelectMany(uncompressedSize =>
                Gen.Elements(true, false).Select(isSystem =>
                    new FileRecord
                    {
                        Name = new string(nameChars),
                        FileId = fileId,
                        Offset = offset,
                        Size = size,
                        UncompressedSize = uncompressedSize,
                        IsSystem = isSystem,
                    }
                )))))));
        }

        /// <summary>
        /// Generates a valid OffsetRecord with a variable number of block keys.
        /// </summary>
        internal static Gen<OffsetRecord> ValidOffsetRecord()
        {
            return Gen.Choose(0, int.MaxValue / 2).Select(x => (long)x).SelectMany(offset =>
                Gen.Choose(1, int.MaxValue / 2).Select(x => (long)x).SelectMany(size =>
                Gen.Elements(
                    BlockType.File, BlockType.Other, BlockType.FormatData,
                    BlockType.FileSystem, BlockType.BlockPadding, BlockType.NJunk, BlockType.XFiller).SelectMany(type =>
                Gen.Choose(0, int.MaxValue / 2).Select(x => (long)x).SelectMany(offsetStart =>
                Gen.Choose(0, 10).SelectMany(blockCount =>
                Gen.ArrayOf(AnyBlockKey(), blockCount).Select(blocks =>
                    new OffsetRecord
                    {
                        Offset = offset,
                        Size = size,
                        Type = type,
                        OffsetStart = offsetStart,
                        Blocks = blocks.Length > 0 ? blocks.ToList() : null,
                    }
                ))))));
        }

        /// <summary>
        /// Generates a valid ImageDirectoryEntry with all fields populated.
        /// </summary>
        internal static Gen<ImageDirectoryEntry> ValidImageDirectoryEntry()
        {
            return Gen.Choose(1, 100_000).Select(x => (long)x).SelectMany(imageId =>
                Gen.Choose(1, 64).SelectMany(nameLength =>
                Gen.ArrayOf(Gen.Choose(0x20, 0x7E).Select(c => (char)c), nameLength).SelectMany(nameChars =>
                Gen.Choose(1, int.MaxValue / 2).Select(x => (long)x).SelectMany(size =>
                Gen.Choose(int.MinValue, int.MaxValue).SelectMany(crc32Raw =>
                Gen.Choose(int.MinValue, int.MaxValue).SelectMany(xxHash64A =>
                Gen.Choose(int.MinValue, int.MaxValue).SelectMany(xxHash64B =>
                Gen.Elements(true, false).SelectMany(hasSystem =>
                Gen.Choose(1, 16).SelectMany(systemLength =>
                Gen.ArrayOf(Gen.Choose(0x41, 0x5A).Select(c => (char)c), systemLength).SelectMany(systemChars =>
                Gen.Elements(
                    ImageFormat.Unknown, ImageFormat.Iso, ImageFormat.Bin,
                    ImageFormat.App, ImageFormat.Cdn, ImageFormat.Gdi,
                    ImageFormat.Folder, ImageFormat.TmdAppFolder).SelectMany(format =>
                Gen.Elements(true, false).SelectMany(hasRollback =>
                Gen.Choose(0, 100).SelectMany(rollbackFileId =>
                Gen.Choose(0, int.MaxValue / 2).Select(x => (long)x).SelectMany(rollbackOffset =>
                Gen.Elements(true, false).SelectMany(removed =>
                Gen.Choose(8192, int.MaxValue / 2).Select(x => (long)x).SelectMany(metadataOffset =>
                Gen.Choose(1, 1_000_000).SelectMany(metadataSize =>
                Gen.Choose(8192, int.MaxValue / 2).Select(x => (long)x).SelectMany(blockMapOffset =>
                Gen.Choose(1, 10_000_000).Select(blockMapSize =>
                    new ImageDirectoryEntry
                    {
                        ImageId = imageId,
                        Name = new string(nameChars),
                        Size = size,
                        Crc32 = (uint)crc32Raw,
                        XxHash64 = ((ulong)(uint)xxHash64A << 32) | (uint)xxHash64B,
                        System = hasSystem ? new string(systemChars) : null,
                        Format = format,
                        RollbackFileId = hasRollback ? rollbackFileId : null,
                        RollbackOffset = hasRollback ? rollbackOffset : null,
                        Removed = removed,
                        MetadataSectionOffset = metadataOffset,
                        MetadataSectionCompressedSize = metadataSize,
                        BlockMapSectionOffset = blockMapOffset,
                        BlockMapSectionCompressedSize = blockMapSize,
                    }
                )))))))))))))))))));
        }

        /// <summary>
        /// Generates a valid BlockIndexEntry with reasonable shard location values.
        /// </summary>
        internal static Gen<BlockIndexEntry> ValidBlockIndexEntry()
        {
            return AnyBlockKey().SelectMany(key =>
                Gen.Choose(0, 100).SelectMany(fileId =>
                Gen.Choose(0, int.MaxValue / 2).Select(x => (long)x).SelectMany(offset =>
                Gen.Choose(1, 0x100000).Select(size =>
                    new BlockIndexEntry
                    {
                        Key = key,
                        FileId = fileId,
                        Offset = offset,
                        Size = size,
                    }
                ))));
        }

        /// <summary>
        /// Represents a set of random blocks for embedded mode round-trip testing.
        /// Each block is a random byte array with varying sizes.
        /// </summary>
        internal record EmbeddedFileContent(byte[][] Blocks);

        /// <summary>
        /// Generates EmbeddedFileContent: a list of 1-8 random blocks with sizes between 64 and 4096 bytes.
        /// Suitable for testing block data round-trip in embedded mode where blocks are written
        /// sequentially and then read back using stored (fileId, offset, length).
        /// </summary>
        internal static Gen<EmbeddedFileContent> ValidEmbeddedFileContent()
        {
            return Gen.Choose(1, 8).SelectMany(blockCount =>
                Gen.ArrayOf(
                    Gen.Choose(64, 4096).SelectMany(size =>
                        Gen.ArrayOf(Gen.Choose(0, 255).Select(b => (byte)b), size)),
                    blockCount)
                .Select(blocks => new EmbeddedFileContent(blocks)));
        }
    }
}