using Spectre.Console;
using System;

namespace Nanook.NKit.App.Cli
{
    /// <summary>
    /// Terminal capability detection for deciding whether interactive prompting is allowed. Uses the
    /// same interactive+ANSI test the live console uses, and refuses when output is redirected so
    /// automation never hangs on a prompt.
    /// </summary>
    public static class TerminalCapabilities
    {
        public static bool IsInteractive =>
            !Console.IsInputRedirected
            && !Console.IsOutputRedirected
            && AnsiConsole.Profile.Out.IsTerminal;
    }
}