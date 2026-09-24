using System;
using System.Collections.Generic;
using System.IO;

namespace Nanook.NKit.SampleProcessingApp.Handlers
{
    /// <summary>
    /// FILE EXTRACT handler. A ported-down version of NKit's ExtractWiiGcStep + ExtractFileHandler,
    /// reimplemented from OUTSIDE the library using only the public read-only section surface.
    ///
    /// Strategy (matches the internal step, minus the shared-extent/overlap optimisation and the
    /// system-file synthesis that need internal format helpers):
    ///  - When a FileSystem area begins (AreaOffset == 0) the whole area's file list is already
    ///    parsed (published as the frozen <see cref="IAreaFileSystemView"/>). We enumerate
    ///    <see cref="IFileSystemView.Files"/> and pre-create the folder tree so empty dirs exist.
    ///  - For every FileSystem block, each <see cref="IReadOnlySectionItem"/> with a non-null
    ///    <see cref="IReadOnlySectionItem.FsFile"/> contributes bytes to that file. We open (or
    ///    reuse) a FileStream for the file and copy the item's slice out with
    ///    <see cref="IReadOnlySection.Read"/>. A file spanning multiple blocks appears as items
    ///    across consecutive blocks; we close it once the accumulated bytes reach its full FsSize.
    /// </summary>
    internal sealed class ExtractHandler : ISectionHandler
    {
        private readonly string _outputRoot;
        private readonly Dictionary<IFsFile, OpenFile> _open = new Dictionary<IFsFile, OpenFile>();
        private int _extracted;
        private int _foldersSeen;

        public ExtractHandler(string outputDirectory, string imageName)
        {
            string baseName = Path.GetFileNameWithoutExtension(imageName);
            this._outputRoot = Path.Combine(outputDirectory, baseName + "_files");
            Directory.CreateDirectory(this._outputRoot);
        }

        public string Name => "extract";

        public void OnSection(IReadOnlySection block)
        {
            if (block.Type != AreaType.FileSystem)
                return;

            // Area start: the FS is fully parsed - pre-create the folder tree.
            if (block.AreaOffset == 0)
            {
                IAreaFileSystemView area = block.AreaFileSystem;
                IFileSystemView primary = area?.Primary;
                if (primary != null)
                {
                    Console.WriteLine($"  [extract] {area.Kind} filesystem: {primary.FileCount} file(s)");
                    for (int i = 0; i < primary.FileCount; i++)
                    {
                        IFsFile file = primary.File(i);
                        string dir = this.MapDirectory(file);
                        Directory.CreateDirectory(dir);
                        this._foldersSeen++;
                    }
                }
            }

            // Stream each file-item's bytes in this block to that file's output stream.
            foreach (IReadOnlySectionItem item in block.Items)
            {
                IFsFile fsFile = item.FsFile;
                if (fsFile == null || item.Size <= 0)
                    continue;

                if (!this._open.TryGetValue(fsFile, out OpenFile of))
                {
                    string path = this.MapFilePath(fsFile);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    of = new OpenFile
                    {
                        Stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 0x200000),
                        Written = 0,
                        FullSize = fsFile.FsSize,
                    };
                    this._open[fsFile] = of;
                }

                block.Read((int)item.FileOffsetInSection, (int)item.Size, of.Stream);
                of.Written += item.Size;

                // File complete - close and count it.
                if (of.Written >= of.FullSize)
                {
                    of.Stream.Flush();
                    of.Stream.Dispose();
                    this._open.Remove(fsFile);
                    this._extracted++;
                }
            }
        }

        public void Completed(NKitTaskResults results)
        {
            // Close any files that never reached full size (truncated/last-file cases).
            foreach (KeyValuePair<IFsFile, OpenFile> kvp in this._open)
            {
                kvp.Value.Stream.Flush();
                kvp.Value.Stream.Dispose();
                this._extracted++;
            }
            this._open.Clear();
            Console.WriteLine($"  [extract] {this._extracted} file(s) written under {this._outputRoot}");
        }

        /// <summary>Full on-disk path for a file, using its logical Path + Name under the output root.</summary>
        private string MapFilePath(IFsFile file)
        {
            string rel = (file.Path ?? string.Empty).TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
            string name = file.IsSystemFile && file.Name != null && file.Name.Length > 2 ? file.Name.Substring(2) : file.Name;
            string dir = string.IsNullOrEmpty(rel) ? this._outputRoot : Path.Combine(this._outputRoot, rel);
            return Path.Combine(dir, name ?? "unnamed");
        }

        /// <summary>Directory for a file's folder (used to pre-create empty folders).</summary>
        private string MapDirectory(IFsFile file)
        {
            string rel = (file.Path ?? string.Empty).TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
            return string.IsNullOrEmpty(rel) ? this._outputRoot : Path.Combine(this._outputRoot, rel);
        }

        private sealed class OpenFile
        {
            public FileStream Stream;
            public long Written;
            public long FullSize;
        }
    }
}
