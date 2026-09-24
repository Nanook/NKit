using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using NKit.Ui.ViewModels.TaskSettings;

namespace NKit.Ui.UserControls.TaskSettings
{
    public partial class ScanSettingsControl : UserControl
    {
        public ScanSettingsControl()
        {
            InitializeComponent();

            DataContext = new ScanSettingsViewModel();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}