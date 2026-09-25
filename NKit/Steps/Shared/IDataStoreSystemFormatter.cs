using NKitDataStore;
using System.Collections.Generic;
using System.IO;

namespace Nanook.NKit.Steps.Shared
{
    internal struct GapRange
    {
        public long ImageOffset { get; set; }
        public long FsOffset { get; set; }
        public int Size { get; set; }
        public DataType DataType { get; set; }
        public byte FillByte { get; set; }
        public GapRange(long imageOffset, long fsOffset, int size, DataType dataType, byte fillByte)
        {
            ImageOffset = imageOffset;
            FsOffset = fsOffset;
            Size = size;
            DataType = dataType;
            FillByte = fillByte;
        }
    }

    /// <summary>
    /// Interface to encapsulate system-specific formatting and offset/stride logic
    /// for writing to the DataStore. Designed to be AOT-friendly (no reflection).
    /// Implementations translate Scan/FS concepts into Image/DataStore concepts
    /// (absolute image offsets, area metadata, stride information, etc.).
    /// Implementations live in the NKit project (steps) because they need access
    /// to NKit types such as ScanArea.
    /// </summary>
    internal interface IDataStoreSystemFormatter : System.IDisposable
    {
        string ImageFileName { get; }

        /// <summary>
        /// Return stride information for a given partition id, or null if the partition is not strided.
        /// </summary>
        DataStride GetStrideForPartition(int partitionId);

        /// <summary>
        /// Return the absolute image offset for the start of a given partition.
        /// </summary>
        long GetPartitionImageOffset(int partitionId);

        /// <summary>
        /// Build AreaMetadata for the provided ScanArea (generic list/key-value style metadata).
        /// </summary>
        AreaMetadata BuildAreaMetadata(ScanArea scanArea);

        /// <summary>
        /// Open a write stream for the provided absolute image offset (image-space offsetStart).
        /// The implementation may use the provided stride to determine how writes are grouped.
        /// </summary>
        Stream BeginFileWrite(long imageOffset, BlockType type, DataStride stride, long? strideOriginOffset = null);

        /// <summary>
        /// Finalize and close a previously opened write stream.
        /// </summary>
        void FinalizeFileWrite(long imageOffset, Stream stream);

        /// <summary>
        /// Convert a filesystem-relative offset (fsOffset) into an absolute image offset using
        /// the partition base image offset.
        /// </summary>
        long ToImageOffsetFromFsOffsets(long partitionImageOffset, long fsOffset);

        /// <summary>
        /// Finalize the section and persist security/block-padding data (decrypted security block or padding) into the datastore.
        /// Earlier API accepted a byte[] payload; new API accepts an ISection and context (isFs/stride) so implementations can read the
        /// appropriate bytes (and avoid allocating temporary arrays in callers).
        /// </summary>
        void FinaliseSectionAndPersistBlockPadding(long imageOffset, ISection section, bool isFs, DataStride stride);

        /// <summary>
        /// Notify the formatter of a processed section (called once per section). Implementations may use this
        /// callback to capture partition header information (e.g. keys/scrub patterns) or update internal state
        /// when the area is set.
        /// </summary>
        void ProcessSection(ISection section);

        /// <summary>
        /// Determines if a file should be preserved in the datastore.
        /// Returns false for files that should be skipped (e.g., junk files, zero-length files).
        /// When a file is not preserved, gap checking will span over it as if it doesn't exist.
        /// </summary>
        bool ShouldPreserveFile(ISection section, IFsFile file);

        // --- Formatter manages area creation and image finalization ---
        /// <summary>
        /// Create area records for the provided scan areas in the image writer owned by the formatter.
        /// </summary>
        void CreateAreas(IEnumerable<ScanArea> areas);

        /// <summary>
        /// Finalize the image using formatter's owned image writer.
        /// </summary>
        void FinalizeImage(long size, uint crc, ulong xxHash);

        /// <summary>
        /// True when the most recent FinalizeImage found the image already existed in the set
        /// (identical name + checksums) and rolled it back rather than storing a duplicate.
        /// Distinguishes a duplicate from a genuine finalize failure.
        /// </summary>
        bool AlreadyExists { get; }

        /// <summary>
        /// Builds a filesystem.yaml from the scan and persists it to the data store.
        /// Each system formatter can sanitise or transform the scan data as needed
        /// before building the FsYaml tree.
        /// </summary>
        /// <param name="scan">The completed scan for the current image.</param>
        void BuildFileSystemYaml(Scan scan);
    }
}