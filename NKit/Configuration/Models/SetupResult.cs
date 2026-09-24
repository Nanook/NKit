namespace Nanook.NKit.Configuration.Models
{
    /// <summary>
    /// Result of configuration setup operations
    /// </summary>
    public class SetupResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public int DirectoriesCreated { get; set; }
        public bool ConfigFileCreated { get; set; }
        public int FixFilesCopied { get; set; }
        public bool ConfigSymbolicLinkCreated { get; set; }
        public bool UserReadmeCreated { get; set; }

        /// <summary>
        /// Indicates if any changes were made during setup
        /// </summary>
        public bool HasChanges => DirectoriesCreated > 0 || ConfigFileCreated || FixFilesCopied > 0 || ConfigSymbolicLinkCreated || UserReadmeCreated;

        /// <summary>
        /// Placeholder files created (for UI compatibility)
        /// </summary>
        public int PlaceholderFilesCreated => 0; // V2 doesn't create placeholder files
    }
}
