using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using NKit.Ui.ViewModels.TaskSettings;

namespace NKit.Ui.UserControls.TaskSettings
{
    public partial class ExtractSettingsControl : UserControl
    {
        public ExtractSettingsControl()
        {
            InitializeComponent();

            DataContext = new ExtractSettingsViewModel();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}