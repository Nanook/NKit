using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using NKit.Ui.ViewModels;

namespace NKit.Ui.UserControls
{
    public partial class UiOptionsControl : UserControl
    {
        public UiOptionsControl()
        {
            InitializeComponent();

            DataContext = new UiOptionsControlViewModel();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}