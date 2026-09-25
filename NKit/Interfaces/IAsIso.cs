using System;
using System.IO;

namespace Nanook.NKit
{
    ///Interface to allow NStream to simply read GC and Wii images in blocks
    ///For GC - anything. For Wii - Header, [gap], Partition Header, 0x800 Partition Data Blocks, [gap] ....
    internal interface IAsIso : IDisposable
    {
        int Construct(Stream stream, bool allowSeek);
        int Read(byte[] buffer, int offset, int count);
        void Complete();
        bool Seekable { get; }
        bool SeekRequired { get; }

        void SetRemovedBlock(Action<MetaData> setBlock);

        long Position { get; set; } //position in file as if it was the full iso
        long Size { get; } //Size of the original image if known
        bool SizeEstimated { get; } //Is the size of the original image estimated
        long RealPosition { get; } //position in the source file
        long RealSize { get; } //size of source file
        ContainerType Format { get; }

        Checksums Checksums { get; }
        Checksums CustomChecksums();
        NKitHeader NKitHeader { get; }

        /// <summary>
        /// A one-line, human-readable summary of the format internals this decoder parsed (header /
        /// table offsets, block sizes, version, counts). Emitted once per source at Detail level by
        /// NKitInput after Construct. Null when the decoder has nothing useful to add. Default is
        /// null so decoders opt in by overriding.
        /// </summary>
        string FormatSummary => null;
    }
}