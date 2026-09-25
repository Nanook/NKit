using Nanook.NKit;
using NKit.Ui.Models;
using Splat;
using System;

namespace NKit.Ui.ViewModels.TaskSettings;

public class VerifySettingsViewModel : TaskSettingsViewModelBase
{
    public VerifySettingsViewModel() : base(Locator.Current.GetService<NKitSettings>())
    {
        MainWindowViewModel.SystemOrTaskUpdatedEvent += SystemOrTaskChangedHandler;
    }

    public void SystemOrTaskChangedHandler(Object sender, EventArgs e)
    {
        if (Settings.Task == TaskType.Verify)
            RefreshVerifySettings();
    }
}