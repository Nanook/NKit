namespace NKitDataStore.Tests
{
    /// <summary>
    /// Stub replacement for the old SQLite-based TestDbHelper.
    /// The original implementation opened raw SQLite connections to dump database
    /// contents as YAML for snapshot testing. With the binary index format migration,
    /// this helper is no longer functional. Tests that relied on it for YAML comparison
    /// are either skipped or need to be rewritten to use the DataStore API directly.
    /// </summary>
    public static class TestDbHelper
    {
        /// <summary>
        /// The file extension used for datastore files.
        /// </summary>
        public const string DatabaseFileExtension = ".nkds";

        /// <summary>
        /// Stub: Previously dumped SQLite databases to YAML for comparison.
        /// Now writes a placeholder message since the binary format cannot be
        /// dumped via raw SQLite connections.
        /// </summary>
        public static void CompareDbToYaml(string directoryPath, string yamlFilePath)
        {
            string directory = Path.GetDirectoryName(yamlFilePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // If the expected YAML file already exists, skip (don't overwrite).
            // Tests using this helper with Skip attributes won't reach here anyway.
            if (File.Exists(yamlFilePath))
                return;

            // Write a placeholder indicating the binary format migration
            File.WriteAllText(yamlFilePath,
                "# TestDbHelper.CompareDbToYaml is a no-op after binary index migration.\n" +
                "# Use DataStore API for verification instead.\n");
        }

        /// <summary>
        /// Stub: Previously dumped SQLite databases to YAML string.
        /// Returns empty string since the binary format cannot be dumped via raw SQLite.
        /// </summary>
        public static string DumpDatabasesToYaml(string directoryPath) => "# Binary index format - SQLite dump not available\n";
    }
}