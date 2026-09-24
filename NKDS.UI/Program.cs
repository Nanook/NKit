using Avalonia;
using Avalonia.Dialogs;
using NkdsUi.Services;
using ReactiveUI.Avalonia;
using System.Text;

namespace NkdsUi;

static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    public static void Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        using SingleInstanceGuard guard = new SingleInstanceGuard();

        if (!guard.IsFirstInstance())
        {
            // Another instance is running — forward args and exit
            guard.SendArgsToRunningInstance(args);
            return;
        }

        // We are the first instance — start listening for args from future instances
        guard.StartListening(remoteArgs =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                if (Avalonia.Application.Current is App app)
                {
                    await app.ProcessRemoteArgsAsync(remoteArgs);
                }
            });
        });

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI(_ => { })
            .UseManagedSystemDialogs();
}