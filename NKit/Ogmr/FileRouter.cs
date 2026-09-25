using System.Collections.Generic;

namespace Nanook.NKit.Ogmr
{
    /// <summary>
    /// Routes input filenames to game entries by testing each filename against
    /// the compiled regex masks in first-match-wins order.
    /// </summary>
    public sealed class FileRouter
    {
        private readonly List<GameEntry> _games;

        /// <summary>
        /// Creates a new <see cref="FileRouter"/> with the specified game entries.
        /// Entries are evaluated in list order during matching.
        /// </summary>
        /// <param name="games">The ordered list of game entries to match against.</param>
        public FileRouter(List<GameEntry> games)
        {
            _games = games;
        }

        /// <summary>
        /// Matches a filename against the compiled regex masks of each game entry in order.
        /// Returns the first matching <see cref="GameEntry"/>, or <c>null</c> if no mask matches.
        /// </summary>
        /// <param name="filename">The filename (without directory path) to match.</param>
        /// <returns>The first matching game entry, or <c>null</c> if no match is found.</returns>
        public GameEntry Match(string filename)
        {
            for (int i = 0; i < _games.Count; i++)
            {
                GameEntry entry = _games[i];
                for (int j = 0; j < entry.CompiledMasks.Length; j++)
                {
                    if (entry.CompiledMasks[j].IsMatch(filename))
                        return entry;
                }
            }

            return null;
        }
    }
}