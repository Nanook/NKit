using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Nanook.NKit;
using NKit.Ui.Models;
using NKit.Ui.Services;
using NKit.Ui.ViewModels;
using Splat;
using System;
using System.Collections.ObjectModel;

namespace NKit.Ui.Views
{
    public partial class MainWindow : Window
    {
        private bool _useNativeTitleBar;

        public MainWindow()
        {
            InitializeComponent();

            // On Linux, apply window decoration mode from persisted settings
            if (OperatingSystem.IsLinux())
            {
                YamlConfigurationStore settingsStore = Locator.Current.GetService<ISettingsStore>() as Services.YamlConfigurationStore;
                string mode = settingsStore?.GetWindowDecorationMode() ?? "csd";

                if (string.Equals(mode, "native", StringComparison.OrdinalIgnoreCase))
                {
                    // Native title bar: let the WM provide decorations
                    WindowDecorations = Avalonia.Controls.WindowDecorations.Full;
                    ExtendClientAreaToDecorationsHint = false;
                    ExtendClientAreaTitleBarHeightHint = -1;
                    _useNativeTitleBar = true;
                    Title = $"NKit v{Nanook.NKit.AppSettings.GetVersion()}";

                    // Hide client-drawn caption buttons (native WM provides its own)
                    StackPanel captionButtons = this.FindControl<Avalonia.Controls.StackPanel>("LinuxCaptionButtons");
                    if (captionButtons != null)
                        captionButtons.IsVisible = false;
                }
                else
                {
                    // CSD mode: owner-drawn chrome provides the visual identity.
                    // Keep Title blank so Avalonia doesn't render it over the logo.
                    WindowDecorations = Avalonia.Controls.WindowDecorations.None;
                    Title = "";
                }
            }
            else
            {
                // Windows / macOS: owner-drawn chrome, no OS title bar visible.
                // Keep Title blank — the custom chrome is the visual identity.
                Title = "";
            }

            DataContext = new MainWindowViewModel();
        }

        private void filter_KeyUp(object sender, KeyEventArgs e)
        {
            if (sender is TextBox filter)
            {
                MainWindowViewModel dataContext = DataContext as MainWindowViewModel;

                string filterText = filter.Text;

                (SystemType systemType, ObservableCollection<SystemType> systemTypes) = dataContext.GetFilteredSystemTypes(filterText, dataContext.SystemTypes);

                dataContext.SystemTypes = systemTypes;

                dataContext.SetTasksFromSystem(systemType);

                dataContext.UpdateSettingsFromStore(systemType);

                refreshUi();
            }
        }

        private void system_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => refreshUi();

        private void task_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => refreshUi();

        private void task_OnTapped(object sender, TappedEventArgs e)
        {
            // This will handle clicks on already selected items
            MainWindowViewModel dataContext = DataContext as MainWindowViewModel;

            // Manually fire the SystemOrTaskUpdatedEvent to trigger settings panel expansion
            // even when clicking on an already selected task
            MainWindowViewModel.SystemOrTaskUpdatedEvent?.Invoke(this, new SystemOrTaskChangedEventArgs
            {
                IsSystemChange = false,
                IsTaskChange = true,
                System = dataContext.SelectedSystem,
                Task = dataContext.SelectedTask
            });

            refreshUi();
        }

        private void refreshUi()
        {
            ListBox lboxTasks = this.Find<ListBox>("lboxTasks");
            ListBox lboxSystems = this.Find<ListBox>("lboxSystems");
            MainWindowViewModel dataContext = DataContext as MainWindowViewModel;

            Dispatcher.UIThread.Post(() =>
            {
                lboxTasks.SelectedItem = dataContext.SelectedTask;
                lboxSystems.SelectedItem = dataContext.SelectedSystem;
                dataContext.RefreshSystemsAndTasks(null);
            });
        }

        private void minimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void maximizeWindow(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void closeWindow(object sender, RoutedEventArgs e) => Close();

        private void titleBar_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (_useNativeTitleBar) return;

            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                if (e.ClickCount == 2)
                {
                    WindowState = WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;
                }
                else
                {
                    BeginMoveDrag(e);
                }
            }
        }

        private void resizeN_PointerPressed(object sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.North, e);
        private void resizeS_PointerPressed(object sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.South, e);
        private void resizeW_PointerPressed(object sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.West, e);
        private void resizeE_PointerPressed(object sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.East, e);
        private void resizeSE_PointerPressed(object sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.SouthEast, e);
        private void resizeSW_PointerPressed(object sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.SouthWest, e);
        private void resizeNE_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                Close();
            else
                BeginResizeDrag(WindowEdge.NorthEast, e);
        }
        private void resizeNW_PointerPressed(object sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.NorthWest, e);
    }
}