using System.Collections.Generic;

namespace Nanook.NKit
{
    /// <summary>
    /// Creates synthetic SourceFile instances that represent folder images
    /// to be built after all child images are processed.
    /// </summary>
    internal static class SyntheticSourceFactory
    {
        /// <summary>
        /// Creates a synthetic SourceFile for each folder group.
        /// The returned SourceFiles have IsSyntheticFolder = true and carry
        /// the FolderGroupInfo in the SyntheticFolderGroup property.
        /// </summary>
        public static List<SourceFile> CreateSyntheticSources(List<FolderGroupInfo> folderGroups)
        {
            List<SourceFile> result = new List<SourceFile>(folderGroups.Count);

            foreach (FolderGroupInfo group in folderGroups)
            {
                result.Add(CreateSynthetic(group));
            }

            return result;
        }

        /// <summary>
        /// Inserts synthetic SourceFiles into the images list, placing each one
        /// immediately after the last child SourceFile it wraps. This ensures
        /// the TmdAppFolder is built as soon as its children are processed,
        /// so a user cancel or crash further down the list won't skip it.
        /// </summary>
        public static void InsertSyntheticSources(List<SourceFile> images, List<FolderGroupInfo> folderGroups)
        {
            foreach (FolderGroupInfo group in folderGroups)
            {
                // Find the index of the last child source in the images list
                int lastChildIndex = -1;
                for (int i = 0; i < images.Count; i++)
                {
                    if (images[i].IsSyntheticFolder)
                        continue;

                    foreach (SourceFile child in group.ChildSources)
                    {
                        if (ReferenceEquals(images[i], child))
                        {
                            lastChildIndex = i;
                            break;
                        }
                    }
                }

                // Insert right after the last child, or append if no children found
                SourceFile synthetic = CreateSynthetic(group);
                if (lastChildIndex >= 0)
                    images.Insert(lastChildIndex + 1, synthetic);
                else
                    images.Add(synthetic);
            }
        }

        private static SourceFile CreateSynthetic(FolderGroupInfo group)
        {
            return new SourceFile
            {
                Name = group.BaseName,
                CleanName = group.BaseName,
                IsSyntheticFolder = true,
                SyntheticFolderGroup = group,
                Status = SourceFileResult.Valid,
                SystemType = group.SystemType,
                // ImageFiles, IndexFile, ArchiveFiles are intentionally null
            };
        }
    }
}