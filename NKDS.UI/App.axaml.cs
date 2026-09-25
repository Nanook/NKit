using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Nanook.NKit.Configuration;
using NkdsUi.Services;
using NkdsUi.ViewModels;
using NkdsUi.Views;

namespace NkdsUi;

public partial class App : Application
{
    private ServiceRegistry? _serviceRegistry;
    private CommandLineProcessor? _commandLineProcessor;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Called from the pipe server when a subsequent instance sends its args.
    /// Delegates to the CommandLineProcessor for accumulation and dispatch.
    /// </summary>
    public async Task ProcessRemoteArgsAsync(string[] args)
    {
        if (_commandLineProcessor != null)
            await _commandLineProcessor.ProcessRemoteArgsAsync(args);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Ensure NKit configuration directories and bundled fix files are set up
        // (creates fix/, keys/ folders and copies default fix YAML files from defaults/)
        EnsureNKitConfigurationSetup();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Instantiate ServiceRegistry exactly once for the application lifetime
            _serviceRegistry = new ServiceRegistry();

            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = new MainWindow(_serviceRegistry);

            // Create the command-line processor with a deferred accessor to the ViewModel
            _commandLineProcessor = new CommandLineProcessor(
                _serviceRegistry,
                () => desktop.MainWindow?.DataContext as MainWindowViewModel);

            // Process command-line arguments after the window is shown
            string[]? args = desktop.Args;
            if (args != null && args.Length > 0)
            {
                desktop.MainWindow.Opened += async (_, _) =>
                {
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await _commandLineProcessor.ProcessArgsAsync(args);
                    }, DispatcherPriority.Background);
                };
            }

            // Dispose ServiceRegistry on application shutdown
            desktop.ShutdownRequested += (_, _) =>
            {
                // Dispose MainWindowViewModel first to unmount any active mounts
                (desktop.MainWindow?.DataContext as MainWindowViewModel)?.Dispose();

                _serviceRegistry?.Dispose();
                _serviceRegistry = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void EnsureNKitConfigurationSetup()
    {
        try
        {
            using ConfigurationManager configManager = new Nanook.NKit.Configuration.ConfigurationManager();
            configManager.EnsureConfiguration();
        }
        catch
        {
            // Non-fatal: if setup fails, paths just won't exist until user configures them
        }
    }
}