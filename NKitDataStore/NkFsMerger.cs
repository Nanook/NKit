namespace NKitDataStore
{
    /// <summary>
    /// Merges system filesystem entries into target per-type NkFs instances.
    /// Operates at the FsYaml level: converts both NkFs to FsYaml, merges trees,
    /// then rebuilds NkFs from the merged FsYaml.
    /// </summary>
    public static class NkFsMerger
    {
        /// <summary>
        /// Merges all entries from the system NkFs into the target NkFs.
        /// Returns a new NkFs containing both the target's original entries
        /// and the system entries (with IsSystem=true).
        /// </summary>
        /// <param name="target">The non-system NkFs to merge into.</param>
        /// <param name="system">The system NkFs containing entries to merge.</param>
        /// <returns>A new NkFs with merged entries.</returns>
        public static NkFs MergeSystemInto(NkFs target, NkFs system)
        {
            FsYaml targetFsYaml = target.ToFsYaml();
            FsYaml systemFsYaml = system.ToFsYaml();

            // Get the root nodes from each FsYaml (the first filesystem root ".")
            FsYamlNode targetRoot = targetFsYaml.FileSystems[0];
            FsYamlNode systemRoot = systemFsYaml.FileSystems[0];

            // Merge system root's children into the target root
            if (systemRoot.Children != null)
            {
                foreach (FsYamlNode systemChild in systemRoot.Children)
                {
                    mergeEntry(systemChild, targetRoot);
                }
            }

            return NkFs.FromFsYaml(targetFsYaml);
        }

        /// <summary>
        /// Merges a single system entry into a target directory node.
        /// - For directories: finds case-insensitive match in target; if found, merges children
        ///   into existing directory; if not found, creates new directory with IsSystem=true.
        /// - For files: inserts with IsSystem=true as a sibling (even if same-named file exists).
        /// </summary>
        private static void mergeEntry(FsYamlNode systemEntry, FsYamlNode targetParent)
        {
            if (systemEntry.IsDirectory)
            {
                // Look for an existing directory with the same name (case-insensitive)
                FsYamlNode? existingDir = findDirectory(targetParent, systemEntry.Name);

                if (existingDir != null)
                {
                    // Merge system directory's children into the existing target directory
                    if (systemEntry.Children != null)
                    {
                        foreach (FsYamlNode systemChild in systemEntry.Children)
                        {
                            mergeEntry(systemChild, existingDir);
                        }
                    }
                }
                else
                {
                    // No matching directory exists — clone the system directory with IsSystem=true
                    FsYamlNode newDir = targetParent.AddDirectory(systemEntry.Name, isSystem: true);
                    if (systemEntry.Children != null)
                    {
                        foreach (FsYamlNode systemChild in systemEntry.Children)
                        {
                            insertSystemEntry(systemChild, newDir);
                        }
                    }
                }
            }
            else
            {
                // File entry: add as sibling with IsSystem=true (even if same-named file exists)
                targetParent.AddFile(
                    systemEntry.Name,
                    systemEntry.Offset,
                    systemEntry.Size,
                    systemEntry.XxHash64,
                    systemEntry.Crc32,
                    isSystem: true);
            }
        }

        /// <summary>
        /// Inserts a system entry (and its subtree) into a target directory,
        /// marking all entries with IsSystem=true. Used when creating new directories
        /// that don't exist in the target tree.
        /// </summary>
        private static void insertSystemEntry(FsYamlNode systemEntry, FsYamlNode targetParent)
        {
            if (systemEntry.IsDirectory)
            {
                FsYamlNode newDir = targetParent.AddDirectory(systemEntry.Name, isSystem: true);
                if (systemEntry.Children != null)
                {
                    foreach (FsYamlNode child in systemEntry.Children)
                    {
                        insertSystemEntry(child, newDir);
                    }
                }
            }
            else
            {
                targetParent.AddFile(
                    systemEntry.Name,
                    systemEntry.Offset,
                    systemEntry.Size,
                    systemEntry.XxHash64,
                    systemEntry.Crc32,
                    isSystem: true);
            }
        }

        /// <summary>
        /// Finds a child directory with the given name using case-insensitive comparison.
        /// Returns null if no matching directory is found.
        /// </summary>
        private static FsYamlNode? findDirectory(FsYamlNode parent, string name)
        {
            if (parent.Children == null)
                return null;

            foreach (FsYamlNode child in parent.Children)
            {
                if (child.IsDirectory && string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }

            return null;
        }
    }
}