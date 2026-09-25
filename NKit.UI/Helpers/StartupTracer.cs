using System;

namespace NKit.Ui.Helpers
{
    /// <summary>
    /// Startup tracer previously wrote early logs to disk. For private distribution without logging,
    /// this implementation is now a no-op to avoid creating files at app startup.
    /// </summary>
    public static class StartupTracer
    {
        // Keep the API but make methods no-op to remove logging.
        public static void Log(string message) { /* no-op */ }
        public static void LogException(string context, Exception ex) { /* no-op */ }
        public static void LogStep(string stepName) { /* no-op */ }
        public static string GetLogPath() => "Logging disabled";
    }
}