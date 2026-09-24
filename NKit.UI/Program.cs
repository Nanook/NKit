using Avalonia;
using NKit.Ui.Helpers;
using ReactiveUI.Avalonia;
using System;
using System.Runtime.InteropServices;

namespace NKit.Ui;

static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            StartupTracer.LogStep("Program.Main() started");
            StartupTracer.Log($"Operating System: {Environment.OSVersion}");
            StartupTracer.Log($"Runtime: {Environment.Version}");
            StartupTracer.Log($"Command Line Args: {string.Join(" ", args)}");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                StartupTracer.Log("Running on macOS");
            }

            StartupTracer.LogStep("Building Avalonia app");
            AppBuilder app = BuildAvaloniaApp();
            StartupTracer.LogStep("Avalonia app built successfully");

            StartupTracer.LogStep("Starting classic desktop lifetime");
            app.StartWithClassicDesktopLifetime(args);

            StartupTracer.LogStep("Application exited normally");
        }
        catch (Exception ex)
        {
            StartupTracer.LogException("Program.Main()", ex);
            throw;
        }
    }


    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI(_ => { })
            .UseSkia();
}