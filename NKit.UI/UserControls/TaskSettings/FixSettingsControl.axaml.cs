using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using NKit.Ui.ViewModels.TaskSettings;

namespace NKit.Ui.UserControls.TaskSettings
{
    public partial class FixSettingsControl : UserControl
    {
        public FixSettingsControl()
        {
            InitializeComponent();

            DataContext = new FixSettingsViewModel();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}