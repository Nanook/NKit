using System.Text.RegularExpressions;

namespace Nanook.NKit.Ogmr
{
    /// <summary>
    /// Represents a single game entry from a 1GMR YAML file, containing the game name,
    /// a filesystem-safe sanitized name, and pre-compiled regex masks for filename matching.
    /// </summary>
    public sealed class GameEntry
    {
        public GameEntry(string name, string[] masks)
        {
            Name = name;
            SanitizedName = FilenameSanitizer.Sanitize(name);
            CompiledMasks = new Regex[masks.Length];
            for (int i = 0; i < masks.Length; i++)
                CompiledMasks[i] = new Regex(masks[i], RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        /// <summary>
        /// The original game name from the YAML file.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Filesystem-safe set name derived from <see cref="Name"/> via <see cref="FilenameSanitizer.Sanitize"/>.
        /// </summary>
        public string SanitizedName { get; }

        /// <summary>
        /// Pre-compiled regex patterns for matching input filenames to this game entry.
        /// Compiled with <see cref="RegexOptions.IgnoreCase"/> | <see cref="RegexOptions.Compiled"/>.
        /// </summary>
        public Regex[] CompiledMasks { get; }
    }
}