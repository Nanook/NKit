using System.Collections.Generic;

namespace Nanook.NKit
{
    // Consolidates ALL filesystem parsing for one NKit format into a single class, replacing the
    // old split where some determination lived in the IImage and the rest in the preprocessor.
    //
    // Division of responsibility:
    //   IImage           - maps the image, detects offsets, does ALL stream reading/seeking, and
    //                      feeds blocks to the reader. It reads the regions the reader asks for.
    //   IFileSystemReader- parses what it is given and reports its state: what it still needs
    //                      (Pending) and whether it is done (Complete). It never touches the stream.
    //
    // The loop the IImage runs:
    //   feed a block -> ProcessBlock -> while (Pending not empty) { read each region, ProcessBlock }
    // For Wii/GC/PS3 the file system precedes the files, so on first contact the reader asks for the
    // whole FST/region-FS up front and is Complete after it is fed. For XBox (and occasionally ISO)
    // directory blocks are pointed AFTER the files that reference them, so the reader keeps reporting
    // Pending offsets as it discovers them and the IImage seeks to each.

    /// <summary>A region of the image the reader still needs, expressed in image offsets.</summary>
    public readonly struct FsRegionRequest
    {
        public FsRegionRequest(long imageOffset, long length)
        {
            this.ImageOffset = imageOffset;
            this.Length = length;
        }

        public long ImageOffset { get; }
        public long Length { get; }
    }

    internal interface IFileSystemReader
    {
        /// <summary>
        /// Feed a block the IImage has read (either a sequential data block or a region previously
        /// reported via <see cref="Pending"/>). The reader parses whatever directory/FST data this
        /// block contains, keyed purely off the buffer's offset.
        /// </summary>
        void ProcessBlock(IBuffer buffer);

        /// <summary>
        /// Regions the reader needs but has not been given yet (unresolved directory/FST pointers).
        /// The IImage seeks to each, reads it, and feeds it back via <see cref="ProcessBlock"/>.
        /// Empty when the reader has everything it needs from the data seen so far.
        /// </summary>
        IReadOnlyList<FsRegionRequest> Pending { get; }

        /// <summary>True once the file system is fully resolved (no pending regions, parse done).</summary>
        bool Complete { get; }

        /// <summary>
        /// When true the reader must resolve the ENTIRE file system before the IImage emits any
        /// data section. This is a CORRECTNESS invariant for the current formats (Wii/GC/WiiU/PS3
        /// store the FS before file data; ISO9660/XBox use an offset-sorted FST whose incremental
        /// parse would insert below already-published indices while parallel section processors
        /// read them). It is therefore hardwired true for every reader today. The incremental
        /// "resolve-through-offset" path in <see cref="FileSystemCoverage"/> remains a reserved
        /// extension point for a hypothetical future format that can safely publish incrementally.
        /// </summary>
        bool RequireFullFileSystemUpFront { get; }
    }
}