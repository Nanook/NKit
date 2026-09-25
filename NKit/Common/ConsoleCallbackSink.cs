using Nanook.NKit.Runtime;
using System;

namespace Nanook.NKit
{
    /// <summary>
    /// Bridges the NKitLog bus back to a host's console callback
    /// (<see cref="Action{String, LogLevel}"/>). The CLI (LiveConsole.write) and UI
    /// (AppendOutputMessage) already behave as sinks; this wraps that callback so the bus is the
    /// single hub while the host's rendering (dynamic footer / UI pane) is preserved byte-for-byte.
    /// <para>
    /// Synchronous by design: the host callbacks (Spectre live console, Avalonia UI append) expect
    /// to run inline on the emitting thread exactly as they did when <c>Log.Write</c> called them
    /// directly. Console/UI writes are low volume (Info + a handful of Detail), so this does not
    /// need the async drain the file sink uses.
    /// </para>
    /// </summary>
    internal sealed class ConsoleCallbackSink : ILogSink, ILevelledSink
    {
        private readonly Action<string, LogLevel> _callback;
        private readonly LogLevel _minimumLevel;
        private readonly Func<int?> _percent;

        public ConsoleCallbackSink(Action<string, LogLevel> callback, LogLevel consoleLevel,
            Func<int?> percent = null)
        {
            _callback = callback;
            _minimumLevel = consoleLevel;
            _percent = percent;
        }

        public LogLevel MinimumLevel => _minimumLevel;

        public void Emit(LogEvent evt)
        {
            if (_callback == null || evt == null) return;

            // Reproduce the legacy Log.Write formatting: a "  > " prefix for the most-verbose
            // (Trace) level, and a trailing newline. Classification is scope + level only.
            string indent = evt.Level == LogLevel.Trace ? "  > " : "";

            // Live progress prefix: [NN%] on every line while a step is running, but only when the
            // console is in Detail mode (same gating as tags — in Info mode the output stays clean).
            string pct = "";
            if ((int)_minimumLevel >= (int)LogLevel.Detail && _percent != null)
            {
                int? p = _percent();
                if (p.HasValue)
                    pct = string.Concat("[", p.Value.ToString(), "%] ");
            }

            // Source tag on the diagnostic levels. The tag comes from the pipeline STAGE scope
            // ([In]/[Pre]/[Proc#N]/[Out]/[Core]…). The Info summary block is left untagged
            // (byte-for-byte legacy output).
            string scopeTag = tagFor(evt);
            string m = string.Concat(indent, pct, scopeTag, evt.Message, Environment.NewLine);

            try { _callback(m, evt.Level); }
            catch { /* a host sink must never abort processing */ }
        }

        // Build the "[tag] " prefix. Only ADDED when the console is running at Detail (or more
        // verbose) — in Info mode our tags are simply not prepended, so the output stays clean and
        // message text (e.g. "[External]", "[Verify/Wii]") is never touched. The tag comes from the
        // SCOPE prefix ([In]/[Core]/[Config]…); the bare root scope ("NKit") is not tagged.
        private string tagFor(LogEvent evt)
        {
            // Info mode: do not add any tag (higher enum value = more verbose; Detail > Info).
            if ((int)_minimumLevel < (int)LogLevel.Detail)
                return "";
            if (evt.Level == LogLevel.Info)
                return "";
            string prefix = ScopePrefix.Resolve(evt.Scope);
            if (string.IsNullOrEmpty(prefix) || prefix == "NKit")
                return "";
            return string.Concat("[", prefix, "] ");
        }

        public void EmitProgress(ILogScope scope, long current, long? total) { }

        public void EmitStatusChange(ILogScope scope, ScopeStatus oldStatus, ScopeStatus newStatus) { }

        public void Flush() { }
    }
}