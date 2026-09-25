namespace Nanook.NKit.SampleProcessingApp.Handlers
{
    /// <summary>
    /// A consumer-side handler for the NKit section feed. An embedder attaches one of these to
    /// <see cref="NKitProcessor.OnSection"/> (via <see cref="OnSection"/>) and, depending on the
    /// task chosen, it does something useful with each processed block WITHOUT NKit writing to disk.
    ///
    /// These are deliberately ported-down versions of NKit's own output Steps (expand/save, CRC,
    /// extract) to prove the public embedding surface is sufficient to reimplement them from
    /// OUTSIDE the library, referencing only public types (no InternalsVisibleTo).
    /// </summary>
    internal interface ISectionHandler
    {
        /// <summary>Short label for logging (e.g. "expand", "crc", "extract").</summary>
        string Name { get; }

        /// <summary>
        /// Called once per processed section block, in strict image order, on the pipeline's
        /// consuming thread. The block is a restricted read-only view: read its bytes with
        /// <see cref="IReadOnlySection.Read"/> / <see cref="IReadOnlySection.ReadBytes"/>, and its
        /// file list via <see cref="IReadOnlySection.AreaFileSystem"/>. Do NOT retain the block past
        /// this call — the backing buffer is pooled and reused for the next block.
        /// </summary>
        void OnSection(IReadOnlySection block);

        /// <summary>
        /// Called once after the run finishes (successfully) so the handler can flush/close its
        /// sink and report a short summary line. <paramref name="results"/> is the final task result
        /// so the handler can cross-check its own work (e.g. compare a computed CRC to results.CRC).
        /// </summary>
        void Completed(NKitTaskResults results);
    }
}
