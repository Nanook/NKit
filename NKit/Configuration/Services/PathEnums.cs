namespace Nanook.NKit.Configuration.Services
{
    /// <summary>
    /// Enumeration for environment variable types to avoid hardcoded strings
    /// </summary>
    internal enum EnvironmentVariableType
    {
        Home,
        XdgConfigHome,
        XdgStateHome,
        XdgDataDirs,
        HomeDrive,
        HomePath,
        UserProfile
    }

    /// <summary>
    /// Enumeration for path component types to avoid hardcoded strings
    /// </summary>
    internal enum PathComponentType
    {
        Library,
        ApplicationSupport,
        Config,
        Local,
        State,
        ApplicationDirectoryName,
        AppExtension,
        Contents,
        MacOS
    }

    /// <summary>
    /// Enumeration for special folder types
    /// </summary>
    internal enum SpecialFolderType
    {
        UserProfile,
        ApplicationData,
        MyDocuments
    }
}
