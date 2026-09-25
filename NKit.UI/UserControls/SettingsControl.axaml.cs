using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using NKit.Ui.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace NKit.Ui.UserControls
{
    public partial class SettingsControl : UserControl
    {
        private readonly TabControl _tcOptions;
        private SettingsControlViewModel _viewModel;
        private bool _isUpdatingTabSelection = false;

        public SettingsControl()
        {
            InitializeComponent();

            _tcOptions = this.Find<TabControl>("tcOptions");
            _viewModel = new SettingsControlViewModel();
            DataContext = _viewModel;

            MainWindowViewModel.SystemOrTaskUpdatedEvent += UpdateTaskSettingsVisibility;

            UpdateTaskSettingsVisibility(null, new SystemOrTaskChangedEventArgs
            {
                IsSystemChange = true,
                IsTaskChange = false,
                System = _viewModel.Settings.System,
                Task = _viewModel.Settings.Task
            });
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void UpdateTaskSettingsVisibility(object sender, SystemOrTaskChangedEventArgs e)
        {
            if (_isUpdatingTabSelection)
                return;

            _isUpdatingTabSelection = true;

            try
            {
                // Get list of currently visible tabs
                List<TabItem> visibleItems = new();

                foreach (TabItem item in _tcOptions.Items)
                {
                    if (item.IsVisible)
                    {
                        visibleItems.Add(item);
                    }
                }

                if (visibleItems.Count == 0)
                    return;

                // The ViewModel handles the tab selection logic based on the change type
                // We just need to ensure the TabControl reflects the ViewModel's selection

                int targetTabIndex = _viewModel.SelectedTabIndex;

                // Validate that the target tab index is within bounds and the tab is visible
                if (targetTabIndex >= 0 && targetTabIndex < _tcOptions.Items.Count)
                {
                    TabItem targetTab = _tcOptions.Items[targetTabIndex] as TabItem;
                    if (targetTab != null && targetTab.IsVisible)
                    {
                        // Tab is valid and visible, select it
                        _tcOptions.SelectedItem = targetTab;
                        return;
                    }
                }

                // If we get here, the target tab is not valid/visible
                // Fall back to first visible tab and update the ViewModel
                TabItem firstVisibleTab = visibleItems.FirstOrDefault();
                if (firstVisibleTab != null)
                {
                    _tcOptions.SelectedItem = firstVisibleTab;

                    // Update the view model with the actual selected index
                    int actualIndex = _tcOptions.Items.IndexOf(firstVisibleTab);
                    if (actualIndex >= 0)
                    {
                        // This will trigger the ViewModel's setter and storage
                        _viewModel.SelectedTabIndex = actualIndex;
                    }
                }
            }
            finally
            {
                _isUpdatingTabSelection = false;
            }
        }
    }
}