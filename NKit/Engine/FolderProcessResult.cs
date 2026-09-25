namespace Nanook.NKit
{
    /// <summary>
    /// Result of processing a synthetic folder SourceFile.
    /// </summary>
    internal enum FolderProcessResult
    {
        /// <summary>Folder image was created successfully.</summary>
        Created,

        /// <summary>Folder image already exists and is up to date.</summary>
        UpToDate,

        /// <summary>No child images found; nothing to aggregate.</summary>
        NoChildren,

        /// <summary>An error occurred during processing.</summary>
        Error,
    }
}