using System;

namespace Nanook.NKit.SampleProcessingApp.Handlers
{
    /// <summary>
    /// CHECKSUM handler. Ported from NKit's Scan.SectionProcessed rolling-CRC logic: rather than
    /// re-hashing the bytes, fold each block's precomputed CRC32 into a running whole-image CRC
    /// using <see cref="Crc.Combine"/> (CRC32 combine over the block size). At the end we compare
    /// our independently-accumulated CRC to the task result's CRC as a correctness check.
    ///
    /// This demonstrates two public capabilities at once: the per-block <see cref="IReadOnlySection.Crc"/>
    /// exposed on the read-only view, and the public <see cref="Crc.Combine"/> utility.
    /// </summary>
    internal sealed class CrcHandler : ISectionHandler
    {
        private uint _combined;
        private bool _first = true;
        private long _size;

        public string Name => "crc";

        public void OnSection(IReadOnlySection block)
        {
            // Skip zero-length blocks (area sentinels) - nothing to combine.
            if (block.Size <= 0)
                return;

            if (this._first)
            {
                this._combined = block.Crc;
                this._first = false;
            }
            else
            {
                // NKit folds CRCs with the pre/post one's-complement convention:
                //   combined = ~Combine(~combined, ~next, nextSize)
                this._combined = ~Crc.Combine(~this._combined, ~block.Crc, block.Size);
            }

            this._size += block.Size;
        }

        public void Completed(NKitTaskResults results)
        {
            Console.WriteLine($"  [crc] computed CRC32 over {this._size:N0} bytes = {this._combined:X8}");
            Console.WriteLine($"  [crc] task result CRC                        = {results.CRC:X8}");
            bool match = this._combined == results.CRC;
            Console.WriteLine($"  [crc] {(match ? "MATCH - section-feed CRC equals NKit's own result" : "MISMATCH - see notes")}");
        }
    }
}
