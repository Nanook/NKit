using Spectre.Console;
using System;

namespace Nanook.NKit.Vfs
{
    /// <summary>Terminal-capability detection for nkds interactive mode.</summary>
    internal static class NkdsTerminal
    {
        public static bool IsInteractive =>
            !Console.IsInputRedirected
            && !Console.IsOutputRedirected
            && AnsiConsole.Profile.Out.IsTerminal;
    }
}
