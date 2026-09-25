using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using NKit.Ui.ViewModels.TaskSettings;

namespace NKit.Ui.UserControls.TaskSettings
{
    public partial class ConvertSettingsControl : UserControl
    {
        public ConvertSettingsControl()
        {
            InitializeComponent();

            DataContext = new ConvertSettingsViewModel();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}