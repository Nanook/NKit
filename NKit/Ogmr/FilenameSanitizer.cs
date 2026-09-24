using System.Text.RegularExpressions;

namespace Nanook.NKit.Ogmr
{
    /// <summary>
    /// Converts game names into valid filesystem-safe filenames by replacing
    /// unsafe characters and normalising the result.
    /// </summary>
    public static class FilenameSanitizer
    {
        private static readonly Regex _unsafeChars = new Regex(@"[:?*<>|""/\\]", RegexOptions.Compiled);
        private static readonly Regex _consecutiveUnderscores = new Regex(@"_{2,}", RegexOptions.Compiled);

        /// <summary>
        /// Sanitizes a game name into a valid filename.
        /// Replaces filesystem-unsafe characters with underscore, collapses consecutive
        /// underscores, trims whitespace and dots, and returns "_unnamed_" if the result is empty.
        /// </summary>
        public static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "_unnamed_";

            // Replace unsafe characters with underscore
            string result = _unsafeChars.Replace(name, "_");

            // Collapse consecutive underscores to a single underscore
            result = _consecutiveUnderscores.Replace(result, "_");

            // Trim leading/trailing whitespace and dots in a loop until stable
            string previous;
            do
            {
                previous = result;
                result = result.Trim().Trim('.');
            } while (result != previous);

            if (string.IsNullOrEmpty(result))
                return "_unnamed_";

            return result;
        }
    }
}