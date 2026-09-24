using System;

namespace Nanook.NKit
{
    public class ProgressEventArgs : EventArgs
    {
        public bool IsStart { get; internal set; }
        public bool IsComplete { get; internal set; }
        public float Progress { get; internal set; }
        public float ProgressTotal { get; internal set; }
        public int Step { get; internal set; }
        public int StepTotal { get; internal set; }
        public string[] Steps { get; internal set; }

        /// <summary>Bytes of the source image processed by this step so far (0 when unknown).</summary>
        public long BytesProcessed { get; internal set; }

        /// <summary>Total bytes this step will process (the source image size; 0 when unknown).</summary>
        public long TotalBytes { get; internal set; }

        /// <summary>Instantaneous read/process throughput in MiB/s over the last sample window (0 when unknown).</summary>
        public double MiBPerSec { get; internal set; }

        /// <summary>
        /// Human-readable step completion detail, set only on the final (Progress==1) event for a
        /// step — e.g. "[CRC 4C7A0149]", "[Combined CRC ...]", "[Partial Read]", "[Failed!]". Mirrors
        /// the text the non-dynamic console appends after the progress dots. Null while in progress.
        /// </summary>
        public string CompleteMessage { get; internal set; }
    }
}