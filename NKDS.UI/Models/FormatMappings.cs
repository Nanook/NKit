namespace NkdsUi.Models;

/// <summary>
/// Defines the valid target export formats for each (System, SourceFormat) combination.
/// </summary>
public static class FormatMappings
{
    /// <summary>
    /// Returns the ordered list of valid target formats for the given system and source format.
    /// Returns a single-element list containing the sourceFormat itself if no specific mapping exists.
    /// </summary>
    public static IReadOnlyList<string> GetTargetFormats(string system, string sourceFormat)
    {
        (string, string) key = (system?.ToLowerInvariant() ?? "", sourceFormat?.ToLowerInvariant() ?? "");
        return key switch
        {
            // Existing entries
            ("gamecube", "iso") => ["iso", "rvz", "ciso", "wbfs"],
            ("wii", "iso") => ["iso", "rvz", "ciso", "wbfs"],
            ("wiiu", "iso") => ["iso", "wux", "app"],
            ("wiiu", "app") => ["app"],
            ("directories", "folder") => ["dir"],

            // Xbox/Xbox360
            ("xbox", "iso") => ["xiso", "iso"],
            ("xbox360", "iso") => ["xiso", "iso"],

            // ISO9660 systems - ISO source
            ("ps1", "iso") => ["iso", "cue"],
            ("ps2", "iso") => ["iso", "cue"],
            ("saturn", "iso") => ["iso", "cue"],
            ("segacd", "iso") => ["iso", "cue"],
            ("psp", "iso") => ["iso", "cue"],
            ("cdi", "iso") => ["iso", "cue"],
            ("pcengine", "iso") => ["iso", "cue"],
            ("default", "iso") => ["iso", "cue"],

            // CUE sources (expand to native format only)
            ("default", "cue") => ["cue"],
            ("dreamcast", "cue") => ["cue", "gdi"],
            ("ps1", "cue") => ["cue"],
            ("ps2", "cue") => ["cue"],
            ("saturn", "cue") => ["cue"],
            ("segacd", "cue") => ["cue"],
            ("cdi", "cue") => ["cue"],
            ("pcengine", "cue") => ["cue"],

            // GDI source (Dreamcast)
            ("dreamcast", "gdi") => ["gdi", "cue"],

            // CHD source (Dreamcast GD-ROM). A GD-ROM CHD has no inherent cue/gdi form — it is
            // stored verbatim with its CHD metadata — so export can produce either representation
            // on demand via the CHD reader + GdRomWriter. Offer both (cue first as the redump default).
            ("dreamcast", "chd") => ["cue", "gdi"],

            // Fallback
            _ => [sourceFormat?.ToLowerInvariant() ?? "iso"],
        };
    }
}