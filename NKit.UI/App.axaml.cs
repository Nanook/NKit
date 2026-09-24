using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Nanook.NKit.Configuration;
using Nanook.NKit.Configuration.Models;
using NKit.Ui.Helpers;
using NKit.Ui.Models;
using NKit.Ui.ViewModels;
using NKit.Ui.Views;
using Splat;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace NKit.Ui
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            StartupTracer.LogStep("App.Initialize() started");

            try
            {
                StartupTracer.LogStep("Loading Avalonia XAML");
                AvaloniaXamlLoader.Load(this);
                StartupTracer.LogStep("Avalonia XAML loaded successfully");

                StartupTracer.LogStep("Calling Bootstrapper.Register()");
                Bootstrapper.Register();
                StartupTracer.LogStep("Bootstrapper.Register() completed successfully");
            }
            catch (Exception ex)
            {
                StartupTracer.LogException("App.Initialize()", ex);
                throw;
            }

            StartupTracer.LogStep("App.Initialize() completed successfully");
        }

        public override void OnFrameworkInitializationCompleted()
        {
            StartupTracer.LogStep("OnFrameworkInitializationCompleted() started");

            try
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    StartupTracer.LogStep("Application is classic desktop lifetime");

                    try
                    {
                        StartupTracer.LogStep("Creating MainWindow");
                        MainWindow mainWindow = new MainWindow();
                        StartupTracer.LogStep("MainWindow created successfully");

                        StartupTracer.LogStep("Creating MainWindowViewModel");
                        MainWindowViewModel viewModel = new MainWindowViewModel();
                        StartupTracer.LogStep("MainWindowViewModel created successfully");

                        StartupTracer.LogStep("Setting MainWindow DataContext");
                        mainWindow.DataContext = viewModel;
                        StartupTracer.LogStep("MainWindow DataContext set successfully");

                        StartupTracer.LogStep("Assigning MainWindow to desktop.MainWindow");
                        desktop.MainWindow = mainWindow;
                        StartupTracer.LogStep("MainWindow assigned successfully - UI should now be visible");
                    }
                    catch (Exception windowEx)
                    {
                        StartupTracer.LogException("MainWindow creation", windowEx);
                        // Do not write window error logs to disk - removed per request
                        throw; // Re-throw window creation errors
                    }
                }
                else
                {
                    StartupTracer.Log("ERROR: ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime");
                    StartupTracer.Log($"ApplicationLifetime type: {ApplicationLifetime?.GetType().Name ?? "null"}");
                }

                StartupTracer.LogStep("Starting configuration setup async");
                ensureConfigurationSetup();

                StartupTracer.LogStep("Calling base.OnFrameworkInitializationCompleted()");
                base.OnFrameworkInitializationCompleted();
                StartupTracer.LogStep("base.OnFrameworkInitializationCompleted() completed");

                StartupTracer.LogStep("Setting up exit handler");
                (App.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime).Exit += OnApplicationExit;
                StartupTracer.LogStep("Exit handler set up successfully");
            }
            catch (Exception ex)
            {
                StartupTracer.LogException("OnFrameworkInitializationCompleted()", ex);
                // Do not write framework initialization fallback logs to disk - removed per request
                throw; // Re-throw framework errors
            }

            StartupTracer.LogStep("OnFrameworkInitializationCompleted() completed successfully");
        }

        private void ensureConfigurationSetup()
        {
            StartupTracer.LogStep("EnsureConfigurationSetupAsync() started");

            try
            {
                using ConfigurationManager configManager = new ConfigurationManager();
                SetupResult setupResult = configManager.EnsureConfiguration();

                StartupTracer.LogStep($"Configuration setup completed - HasChanges: {setupResult.HasChanges}");

                if (setupResult.HasChanges)
                {
                    string details = $"Created {setupResult.DirectoriesCreated} directories, Config file: {setupResult.ConfigFileCreated}, Fix files: {setupResult.FixFilesCopied}, Placeholder files: {setupResult.PlaceholderFilesCreated}";
                    StartupTracer.Log($"Configuration setup details: {details}");
                    System.Diagnostics.Debug.WriteLine($"Configuration setup: {details}");
                }
            }
            catch (System.Exception ex)
            {
                StartupTracer.LogException("EnsureConfigurationSetupAsync()", ex);
                System.Diagnostics.Debug.WriteLine($"Configuration setup failed: {ex.Message}");
            }

            StartupTracer.LogStep("EnsureConfigurationSetupAsync() completed");
        }

        public void OnApplicationExit(object sender, ControlledApplicationLifetimeExitEventArgs e)
        {
            StartupTracer.LogStep("OnApplicationExit() started");

            NKitSettings settings = Locator.Current.GetService<NKitSettings>();
            ISettingsStore settingsStore = Locator.Current.GetService<ISettingsStore>();

            if (settings != null && settingsStore != null)
            {
                settingsStore.StoreSettings(settings);
                settingsStore.WriteSettingsToDisk();

                if (settingsStore.UiSettings.PersistFileQueue)
                {
                    ObservableCollection<SourceFileRecord> fileQueue = Locator.Current.GetService<ObservableCollection<SourceFileRecord>>();

                    if (fileQueue != null && fileQueue.Any(x => x.ProcessingStatus == ProcessingStatus.Processing))
                        fileQueue.ForEach(x => x.ProcessingStatus = x.ProcessingStatus == ProcessingStatus.Processing ? ProcessingStatus.Cancelled : x.ProcessingStatus);

                    if (fileQueue != null)
                        settingsStore.WriteFileQueueToDisk(fileQueue);
                }
                else
                {
                    if (File.Exists(Bootstrapper.UserQueuePath))
                        File.Delete(Bootstrapper.UserQueuePath);
                }
            }
            else
            {
                StartupTracer.Log($"WARNING: Could not save settings on exit - settings: {settings != null}, settingsStore: {settingsStore != null}");
            }

            StartupTracer.LogStep("OnApplicationExit() completed");
        }
    }
}