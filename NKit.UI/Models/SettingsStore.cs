using Nanook.NKit;
using System.Collections.ObjectModel;

namespace NKit.Ui.Models
{
    public interface ISettingsStore
    {
        float Version { get; set; }
        string LastFolderBrowsed { get; set; }
        UiSettings UiSettings { get; set; }

        void StoreSettings(NKitSettings settings);
        void WriteSettingsToDisk();
        void WriteFileQueueToDisk(ObservableCollection<SourceFileRecord> fileQueue);
        NKitSettings GetLastUsedSettings(SystemType system);
        NKitSettings GetSettings(SystemType systemType, TaskType taskType);
    }


}