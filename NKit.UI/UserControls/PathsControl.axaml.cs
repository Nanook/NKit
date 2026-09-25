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
    public partial class PathsControl : UserControl
    {
        public PathsControl()
        {
            InitializeComponent();

            DataContext = new PathsControlViewModel();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        public async void OnSelectBaseInFolderClicked(object sender, RoutedEventArgs args)
        {
            PathsControlViewModel dataContext = DataContext as PathsControlViewModel;
            string newPath = await GetFolderPath("Select Base In Folder", dataContext.Settings.BaseInPath);
            if (newPath != null)
            {
                dataContext.Settings.BaseInPath = newPath;
            }
        }

        public async void OnSelectDatFolderClicked(object sender, RoutedEventArgs args)
        {
            PathsControlViewModel dataContext = DataContext as PathsControlViewModel;
            string newPath = await GetFolderPath("Select Dat Folder", dataContext.Settings.Dat);
            if (newPath != null)
            {
                dataContext.Settings.Dat = newPath;
            }
        }

        public async void OnSelectTempFolderClicked(object sender, RoutedEventArgs args)
        {
            PathsControlViewModel dataContext = DataContext as PathsControlViewModel;
            string newPath = await GetFolderPath("Select Temp Folder", dataContext.Settings.Tmp);
            if (newPath != null)
            {
                dataContext.Settings.Tmp = newPath;
            }
        }

        public async void OnSelectOutputFolderClicked(object sender, RoutedEventArgs args)
        {
            PathsControlViewModel dataContext = DataContext as PathsControlViewModel;
            string newPath = await GetFolderPath("Select Output folder", dataContext.Settings.Out);
            if (newPath != null)
            {
                dataContext.Settings.Out = newPath;
            }
        }

        public async void OnSelectFixInfoFileClicked(object sender, RoutedEventArgs args)
        {
            PathsControlViewModel dataContext = DataContext as PathsControlViewModel;
            string newPath = await GetFilePath("Select Fix Info File", dataContext.Settings.FixInfo);
            if (newPath != null)
            {
                dataContext.Settings.FixInfo = newPath;
            }
        }

        public async void OnSelectFixFilesFolderClicked(object sender, RoutedEventArgs args)
        {
            PathsControlViewModel dataContext = DataContext as PathsControlViewModel;
            string newPath = await GetFolderPath("Select Fix Files Folder", dataContext.Settings.FixFiles);
            if (newPath != null)
            {
                dataContext.Settings.FixFiles = newPath;
            }
        }

        public async void OnSelectKeysPathClicked(object sender, RoutedEventArgs args)
        {
            PathsControlViewModel dataContext = DataContext as PathsControlViewModel;
            string newPath = await GetFolderPath("Select Keys Folder", dataContext.Settings.Keys);
            if (newPath != null)
            {
                dataContext.Settings.Keys = newPath;
            }
        }

        public async void OnSelectKeysArchiveClicked(object sender, RoutedEventArgs args)
        {
            PathsControlViewModel dataContext = DataContext as PathsControlViewModel;
            string newPath = await GetFilePath("Select Keys Archive", dataContext.Settings.Keys);
            if (newPath != null)
            {
                dataContext.Settings.Keys = newPath;
            }
        }

        public async void OnSelectScanInFolderClicked(object sender, RoutedEventArgs args)
        {
            PathsControlViewModel dataContext = DataContext as PathsControlViewModel;
            string newPath = await GetFolderPath("Select Scan In Folder", dataContext.Settings.ScanIn);
            if (newPath != null)
            {
                dataContext.Settings.ScanIn = newPath;
            }
        }

        public async void OnSelectScanOutFolderClicked(object sender, RoutedEventArgs args)
        {
            PathsControlViewModel dataContext = DataContext as PathsControlViewModel;
            string newPath = await GetFolderPath("Select Scan Out Folder", dataContext.Settings.ScanOut);
            if (newPath != null)
            {
                dataContext.Settings.ScanOut = newPath;
            }
        }

        public async void OnSelectRedumpDatsFolderClicked(object sender, RoutedEventArgs args)
        {
            PathsControlViewModel dataContext = DataContext as PathsControlViewModel;
            string newPath = await GetFolderPath("Select Redump Dats Folder", dataContext.Settings.RedumpDatsPath);
            if (newPath != null)
            {
                dataContext.Settings.RedumpDatsPath = newPath;
            }
        }

        public async void OnSelectNoIntroDatsFolderClicked(object sender, RoutedEventArgs args)
        {
            PathsControlViewModel dataContext = DataContext as PathsControlViewModel;
            string newPath = await GetFolderPath("Select NoIntro Dats Folder", dataContext.Settings.NoIntroDatsPath);
            if (newPath != null)
            {
                dataContext.Settings.NoIntroDatsPath = newPath;
            }
        }

        public async void OnSelectTosecDatsFolderClicked(object sender, RoutedEventArgs args)
        {
            PathsControlViewModel dataContext = DataContext as PathsControlViewModel;
            string newPath = await GetFolderPath("Select TOSEC Dats Folder", dataContext.Settings.TosecDatsPath);
            if (newPath != null)
            {
                dataContext.Settings.TosecDatsPath = newPath;
            }
        }

        public async void OnSelectDatPathClicked(object sender, RoutedEventArgs args)
        {
            PathsControlViewModel dataContext = DataContext as PathsControlViewModel;
            string newPath = await GetFolderPath("Select Dat Path", dataContext.Settings.DatPath_Manual);
            if (newPath != null)
            {
                dataContext.Settings.DatPath_Manual = newPath;
            }
        }

        TopLevel GetWindow() => TopLevel.GetTopLevel(this);

        public async Task<string> GetFolderPath(string title, string initialDirectory)
        {
            PathsControlViewModel dataContext = DataContext as PathsControlViewModel;
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
            PathsControlViewModel dataContext = DataContext as PathsControlViewModel;
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
    }
}