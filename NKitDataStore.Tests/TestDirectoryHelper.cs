namespace NKitDataStore.Tests
{
    public static class TestDirectoryHelper
    {
        private static readonly Lazy<string> _projectDirectory = new Lazy<string>(FindProjectDirectory);

        public static string ProjectDirectory => _projectDirectory.Value;

        private static string FindProjectDirectory()
        {
            string currentDirectory = AppContext.BaseDirectory;

            // First, try to find the .csproj file by walking up from the bin directory
            while (currentDirectory != null)
            {
                if (Directory.GetFiles(currentDirectory, "*.csproj").Length > 0)
                {
                    return currentDirectory;
                }
                currentDirectory = Directory.GetParent(currentDirectory)?.FullName;
            }

            // Fallback: look for solution directory
            currentDirectory = AppContext.BaseDirectory;
            while (currentDirectory != null)
            {
                if (Directory.GetFiles(currentDirectory, "*.sln").Length > 0)
                {
                    // We found the solution directory. The test project is typically a subdirectory.
                    // This assumes a standard project layout.
                    string testProjectDirectory = Path.Combine(currentDirectory, "NKitDataStore.Tests");
                    if (Directory.Exists(testProjectDirectory))
                    {
                        return testProjectDirectory;
                    }
                    // Fallback to solution directory if the test project isn't found directly
                    return currentDirectory;
                }
                currentDirectory = Directory.GetParent(currentDirectory)?.FullName;
            }

            // Fallback if the .sln is not found
            return AppContext.BaseDirectory;
        }
    }
}