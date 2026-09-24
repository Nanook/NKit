namespace Nanook.NKit.Steps.Shared
{
    /// <summary>
    /// Implemented by system formatters that handle index-based formats (TMD, CUE, GDI)
    /// to provide image name disambiguation when multiple index files produce the same name.
    /// </summary>
    public interface IIndexNameDisambiguator
    {
        /// <summary>
        /// Returns true if this formatter supports index-based name disambiguation.
        /// </summary>
        bool SupportsIndexDisambiguation { get; }

        /// <summary>
        /// Disambiguates an image name by appending the index filename in brackets.
        /// </summary>
        /// <param name="baseName">The base image name (e.g., "Game Title")</param>
        /// <param name="indexFileName">The index file name (e.g., "tmd.0", "game.cue")</param>
        /// <returns>Disambiguated name (e.g., "Game Title [tmd.0]")</returns>
        string DisambiguateImageName(string baseName, string indexFileName);

        /// <summary>
        /// Restores the base name from a disambiguated image name by stripping the
        /// index suffix. Returns the original name unchanged if it was not disambiguated.
        /// </summary>
        /// <param name="disambiguatedName">The disambiguated name (e.g., "Game Title [tmd.0]")</param>
        /// <returns>The base name (e.g., "Game Title"), or the input unchanged</returns>
        string RestoreBaseName(string disambiguatedName);
    }
}