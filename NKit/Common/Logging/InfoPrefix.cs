namespace Nanook.NKit
{
    /// <summary>
    /// Machine-readable leading prefixes for the Info-level SUMMARY lines a task emits
    /// (image title, input params, output params). These are NOT logging scopes — they are
    /// literal leading tokens baked into the message TEXT (e.g. <c>"[Title] ..."</c>).
    /// <para>
    /// Their only purpose is to let a host console CLASSIFY a summary line so it can colour it.
    /// The contract with the host is level-driven:
    /// </para>
    /// <list type="bullet">
    /// <item>At <b>Info</b> verbosity the host recognises the prefix, removes it from the
    /// displayed text, and colours the line by which prefix it was.</item>
    /// <item>At <b>Detail</b>/<b>Debug</b> verbosity the host leaves the prefix in place and does
    /// not recolour — the raw tagged text is shown alongside the stage tags.</item>
    /// <item>A line WITHOUT one of these exact prefixes is passed through unchanged and uncoloured.</item>
    /// </list>
    /// <para>
    /// The library only stamps the prefix; it never chooses colours and pulls in no presentation
    /// types. Hosts that do not care (file sink, plain redirect) simply see the prefix as ordinary
    /// leading text.
    /// </para>
    /// </summary>
    public static class InfoPrefix
    {
        /// <summary>The image title line — carries the <c>[Task/System]  Name</c> title (and, when
        /// known, the <c>X/Y</c> image counter). Kept in the exact current title format after the
        /// prefix is stripped, so a host can further sub-colour the title parts.</summary>
        public const string Title = "[Title]";

        /// <summary>An input-parameter summary line (InFile / OutPath / SrcType / checksums / Verify …).</summary>
        public const string InParam = "[InParam]";

        /// <summary>An output-result summary line (DatName / OutFile / OutKey / Verify result …).</summary>
        public const string OutParam = "[OutParam]";

        /// <summary>Prepend a prefix (with a single trailing space) to a message body.</summary>
        public static string Stamp(string prefix, string message) => string.Concat(prefix, " ", message);
    }
}
