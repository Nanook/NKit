using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using NKit.Ui.ViewModels.TaskSettings;

namespace NKit.Ui.UserControls.TaskSettings
{
    public partial class DedupeSettingsControl : UserControl
    {
        public DedupeSettingsControl()
        {
            InitializeComponent();

            DataContext = new DedupeSettingsViewModel();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}