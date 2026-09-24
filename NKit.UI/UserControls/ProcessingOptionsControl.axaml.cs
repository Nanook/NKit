using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using NKit.Ui.ViewModels;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NKit.Ui.UserControls
{
    public partial class ProcessingOptionsControl : UserControl
    {
        public ProcessingOptionsControl()
        {
            InitializeComponent();

            DataContext = new ProcessingOptionsViewModel();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        TopLevel GetWindow() => TopLevel.GetTopLevel(this);

        public async Task<string> GetFolderPath(string title, string initialDirectory)
        {
            ProcessingOptionsViewModel dataContext = DataContext as ProcessingOptionsViewModel;
            IStorageProvider storageProvider = GetWindow().StorageProvider;

            IReadOnlyList<IStorageFolder> folder = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = Directory.Exists(initialDirectory)
                    ? await storageProvider.TryGetFolderFromPathAsync(initialDirectory)
                    : !string.IsNullOrEmpty(dataContext.SettingsStore.LastFolderBrowsed)
                        ? await storageProvider.TryGetFolderFromPathAsync(dataContext.SettingsStore.LastFolderBrowsed)
                        : null
            });

            if (folder?.FirstOrDefault() != null)
            {
                string folderPath = folder.First().Path.LocalPath;
                dataContext.SettingsStore.LastFolderBrowsed = folderPath;
                return folderPath;
            }

            return null;
        }

        public async Task<string> GetFilePath(string title, string initialDirectory)
        {
            ProcessingOptionsViewModel dataContext = DataContext as ProcessingOptionsViewModel;
            IStorageProvider storageProvider = GetWindow().StorageProvider;

            IReadOnlyList<IStorageFile> files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = new[] { FilePickerFileTypes.All },
                SuggestedStartLocation = File.Exists(initialDirectory)
                    ? await storageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(initialDirectory))
                    : !string.IsNullOrEmpty(dataContext.SettingsStore.LastFolderBrowsed)
                        ? await storageProvider.TryGetFolderFromPathAsync(dataContext.SettingsStore.LastFolderBrowsed)
                        : null
            });

            if (files?.FirstOrDefault() != null)
            {
                string filePath = files.First().Path.LocalPath;
                dataContext.SettingsStore.LastFolderBrowsed = Path.GetDirectoryName(filePath);
                return filePath;
            }

            return null;
        }

        public async void OnSelectResultsFolderClicked(object sender, RoutedEventArgs args)
        {
            ProcessingOptionsViewModel dataContext = DataContext as ProcessingOptionsViewModel;
            // use manual path (absolute) as suggested start location
            string initial = dataContext.Settings.ResultsOutPath_Manual ?? dataContext.Settings.ResultsOut;
            string newPath = await GetFolderPath("Select Results Folder", initial);
            if (newPath != null)
            {
                dataContext.Settings.ResultsOutPath_Manual = newPath;
            }
        }

        public async void OnSelectLogFileClicked(object sender, RoutedEventArgs args)
        {
            ProcessingOptionsViewModel dataContext = DataContext as ProcessingOptionsViewModel;
            string initial = dataContext.Settings.LogOutPath_Manual ?? dataContext.Settings.LogOut;
            string newPath = await GetFolderPath("Select Log File", initial);
            if (newPath != null)
            {
                dataContext.Settings.LogOutPath_Manual = newPath;
            }
        }
    }
}