using Avalonia.Platform.Storage;
using Nanook.NKit;
using System.Collections.Generic;

namespace NKit.Ui.Helpers
{
    public static class FileExtensionsHelper
    {
        public static List<FilePickerFileType> GetFileDialogFilters()
            => new List<FilePickerFileType>
                {
                    FilePickerFileTypes.All,
                    new("Supported Types") { Patterns = SourceFiles.SupportedFilePatterns },
                    new("Images") { Patterns = SourceFiles.ImageFilePatterns },
                    new("Archives") { Patterns = SourceFiles.ArchiveFilePatterns },
                };
    }
}