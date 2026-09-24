using System;
using System.IO;

namespace Nanook.NKit.SampleProcessingApp.Handlers
{
    /// <summary>
    /// EXPAND / SAVE handler. Ported from NKit's ScanStep expand path: write every block's bytes to
    /// an output file in image order, reconstructing the full decoded image on the consumer's disk.
    ///
    /// The NKit Step appends section.Encrypted to its own ChecksumStream; here we don't have the raw
    /// buffer (deliberately - it's pooled/internal), so we copy each block out via
    /// <see cref="IReadOnlySection.Read"/> straight into our own FileStream. Same result: the bytes
    /// arrive in order and we lay them down contiguously.
    /// </summary>
    internal sealed class SaveHandler : ISectionHandler
    {
        private readonly string _outputPath;
        private FileStream _out;
        private long _written;

        public SaveHandler(string outputDirectory, string imageName)
        {
            Directory.CreateDirectory(outputDirectory);
            // Keep the source name; the decoded stream is an ISO-like image, so ".iso" is a fair
            // generic extension for the sample. A real embedder would pick per system.
            string baseName = Path.GetFileNameWithoutExtension(imageName);
            this._outputPath = Path.Combine(outputDirectory, baseName + ".expanded.iso");
        }

        public string Name => "expand";

        public void OnSection(IReadOnlySection block)
        {
            if (this._out == null)
                this._out = new FileStream(this._outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 0x200000);

            // Copy the whole block (fs-space 0..Size) into our output stream. For non-hashed areas
            // Size == fs size; the pipeline hands blocks in contiguous image order so a simple
            // append reproduces the decoded image.
            block.Read(0, (int)block.Size, this._out);
            this._written += block.Size;
        }

        public void Completed(NKitTaskResults results)
        {
            this._out?.Flush();
            this._out?.Dispose();
            this._out = null;
            Console.WriteLine($"  [expand] wrote {this._written:N0} bytes -> {this._outputPath}");
        }
    }
}
