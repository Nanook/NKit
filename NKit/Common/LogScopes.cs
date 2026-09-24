using System.Text;

namespace Nanook.NKit
{
    /// <summary>
    /// Central definition of every log SCOPE prefix and COMPONENT tag used across NKit logging.
    /// Using these constants (instead of string literals) means a "find references" on a name
    /// locates every log line that uses that tag — the primary aid when debugging a specific stage
    /// or component.
    /// <para>
    /// Two kinds of tag:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Scopes</b> — the pipeline STAGE a line belongs to (<c>[In]</c>/<c>[Pre]</c>/
    /// <c>[Out]</c>) or a cross-cutting phase (<c>[Config]</c>/<c>[Input]</c>). Passed to
    /// <c>Log.ScopeFor(...)</c>; the sink renders it as the first bracket.</item>
    /// <item><b>Components</b> — WHICH class wrote the line (e.g. <c>[CSO]</c>, <c>[RVZ]</c>,
    /// <c>[Image]</c>). Prepended to the message via <see cref="Tag"/> so the rendered line reads
    /// <c>[In] [CSO] message</c>. Needed because several IAsIso decoders/readers can be active
    /// (even nested) and otherwise a line does not say which one emitted it.</item>
    /// </list>
    /// </summary>
    internal static class LogScopes
    {
        // ── Pipeline stage scopes (Log.ScopeFor) ────────────────────────────────
        public const string In = "In";     // section create / image read
        public const string Pre = "Pre";   // pre-process (serial)
        public const string Out = "Out";   // ordered output/write

        // ── Cross-cutting phase scopes (Log.ScopeFor) ───────────────────────────
        public const string Config = "Config"; // config / path / system resolution
        public const string Input = "Input";   // input detection / scanning
        public const string Core = "Core";     // NKitCore engine / worker-pool scaling
        public const string Params = "Params"; // effective run-parameters banner
        public const string Results = "Results"; // outcome / result summary lines

        // ── Component tags (prepended to a message via Tag) ─────────────────────
        // IAsIso container decoders:
        public const string Rvz = "RVZ";
        public const string Wia = "WIA";
        public const string NKit = "NKit";
        public const string Wbfs = "WBFS";
        public const string Ciso = "CISO";
        public const string CsoZso = "CSO";   // Cso/Zso share
        public const string Gcz = "GCZ";
        public const string Dax = "DAX";
        public const string Jso = "JSO";
        public const string Wux = "WUX";
        public const string Chd = "CHD";
        public const string IsoDec = "IsoDec";
        public const string Default = "ISO";
        public const string TmdApp = "TmdApp";
        public const string DataStore = "DataStore";
        public const string GcFix = "GcFix";
        public const string WiiFix = "WiiFix";
        public const string Ps3Fix = "Ps3Fix";

        // IImage readers (the disc-format reader that walks areas), by format family:
        public const string WiiGc = "WiiGc";     // Wii + GameCube reader
        public const string WiiU = "WiiU";
        public const string Iso9660 = "ISO9660"; // PS1/PS2/PS3/PSP/Dreamcast/… reader
        public const string XDvdFs = "XDVDFS";   // XBox / XBox360 reader
        public const string Section = "Section"; // SectionProcessor per-area summary

        // Shared input plumbing:
        public const string BufferStream = "BufferStream"; // seekable/cached view over the source
        public const string SourceFile = "SourceFile";     // source-file scan + streaming (parts/index/archive)
        public const string Archive = "Archive";           // archive open / entry read (zip/7z/rar/gz)
        public const string SplitStream = "SplitStream";   // multi-part / split file stitching

        // Decision-logic tags (heuristics / classifiers whose wrong choice is hard to diagnose):
        public const string Security = "Security";     // key brute-force, encryption heuristic, cert/hash validation
        public const string CalcConfig = "CalcConfig"; // NKitTaskContext.CalculateConfig out-format derivation

        // ── Output-stage step tags (prepended via Tag, rendered "[Out] [Name] …") ───────────────
        // One per concrete IStep class; a per-step completion summary carries its tag so a log
        // filtered on e.g. [FixWiiGc] shows only that step's lines.
        // Scan / verify / straight write:
        public const string StepScan = "Scan";                 // ScanStep (Scan/Verify/Convert-Image/Expand-Image)
        public const string StepScanPatch = "ScanPatch";       // ScanPatchStep
        public const string StepScanIsoGd = "ScanIsoGd";       // ScanIsoGdStep (Verify-IsoGdChd)
        // Fix:
        public const string StepFixWiiGc = "FixWiiGc";         // FixWiiGcStep
        public const string StepFixExtractWiiGc = "FixExtractWiiGc"; // FixExtractWiiGcStep
        public const string StepFixPs3Ird = "FixPs3Ird";       // FixPs3IrdStep
        public const string StepFixExtractPs3 = "FixExtractPs3"; // FixExtractPs3Step
        // Convert:
        public const string StepConvertWiiGcCiso = "ConvertWiiGcCiso"; // ConvertWiiGcCisoStep
        public const string StepConvertWiiGcWbfs = "ConvertWiiGcWbfs"; // ConvertWiiGcWbfsStep
        public const string StepConvertWiiGcRvz = "ConvertWiiGcRvz";   // ConvertWiiGcRvzStep
        public const string StepConvertWiiUWux = "ConvertWiiUWux";     // ConvertWiiUWuxStep
        public const string StepConvertWiiUAppTmd = "ConvertWiiUAppTmd"; // ConvertWiiUAppTmdStep
        public const string StepConvertXBox = "ConvertXBox";           // ConvertXBoxStep
        public const string StepConvertIsoCsoZso = "ConvertIsoCsoZso"; // ConvertIsoCsoZsoStep
        public const string StepConvertIsoDecIso = "ConvertIsoDecIso"; // ConvertIsoDecIsoStep
        public const string StepConvertIsoCueToc = "ConvertIsoCueToc"; // ConvertIsoCueTocStep
        public const string StepConvertIsoCueGdi = "ConvertIsoCueGdi"; // ConvertIsoCueGdiFromGdStep
        // Expand:
        public const string StepExpandWiiUAppTmd = "ExpandWiiUAppTmd"; // ExpandWiiUAppTmdStep
        public const string StepExpandXBox = "ExpandXBox";             // ExpandXBoxStep
        // Extract:
        public const string StepExtractWiiGc = "ExtractWiiGc";   // ExtractWiiGcStep
        public const string StepExtractWiiU = "ExtractWiiU";     // ExtractWiiUStep
        public const string StepExtractIso = "ExtractIso";       // ExtractIsoStep
        public const string StepExtractXBox = "ExtractXBox";     // ExtractXBoxStep
        public const string StepExtractForensic = "ExtractForensic"; // ExtractForensicStep
        // Dedupe / datastore:
        public const string StepDedupe = "Dedupe";              // DedupeStep

        // Verify decision engine (NKitVerify: method + checksum selection and the verify outcome):
        public const string Verify = "Verify";
        // Wipe:
        public const string StepWipeIso = "WipeIso";           // WipeIsoStep
        public const string StepWipeIsoGdChd = "WipeIsoGdChd"; // WipeIsoGdChdStep
        public const string StepWipeWiiGc = "WipeWiiGc";       // WipeWiiGcStep
        public const string StepWipeWiiU = "WipeWiiU";         // WipeWiiUStep

        /// <summary>Format a component tag as a leading "[Component] " for prepending to a message,
        /// so the rendered line reads e.g. "[In] [CSO] ...".</summary>
        public static string Tag(string component) => string.Concat("[", component, "] ");

        /// <summary>
        /// Masked identifier for a key, safe to log: the first 2 bytes rendered as 4 hex chars
        /// followed by "..." — enough to distinguish which candidate key was tried (works for binary
        /// AES keys too) without revealing the key material. Returns "(null)" / "(empty)" as needed.
        /// </summary>
        public static string MaskKey(byte[] key)
        {
            if (key == null)
                return "(null)";
            if (key.Length == 0)
                return "(empty)";
            int n = System.Math.Min(2, key.Length);
            StringBuilder sb = new System.Text.StringBuilder((n * 2) + 3);
            for (int i = 0; i < n; i++)
                sb.Append(key[i].ToString("X2"));
            sb.Append("...");
            return sb.ToString();
        }
    }
}