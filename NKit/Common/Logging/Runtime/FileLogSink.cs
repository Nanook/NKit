using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Nanook.NKit.Runtime
{
    /// <summary>
    /// Append-only file sink. Reproduces the legacy <c>Log</c> file behaviour: append mode,
    /// <see cref="FileShare.ReadWrite"/> sharing, retry on a transient open lock, and a
    /// <see cref="FileWriteError"/> flag that latches (and stops further writes) on failure.
    /// <para>
    /// All I/O runs on the <see cref="AsyncLogSink"/> drain thread, so writing here never blocks a
    /// worker. Each line is timestamped and prefixed with the scope-derived source tag; the full
    /// scope chain is appended when it adds information.
    /// </para>
    /// </summary>
    internal sealed class FileLogSink : AsyncLogSink
    {
        private readonly string _path;
        private StreamWriter _writer;

        public bool FileWriteError { get; private set; }

        public FileLogSink(string path, LogLevel minimumLevel = LogLevel.Detail, int capacity = 8192)
            : base(minimumLevel, capacity)
        {
            _path = path;
            Open();
        }

        private void Open()
        {
            if (string.IsNullOrEmpty(_path)) { FileWriteError = true; return; }

            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(_path));
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                const int maxAttempts = 3;
                const int delayMs = 150;
                int attempt = 0;
                while (true)
                {
                    try
                    {
                        FileStream fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 0x200000);
                        _writer = new StreamWriter(fs);
                        return;
                    }
                    catch (IOException) when (attempt < maxAttempts - 1)
                    {
                        attempt++;
                        Thread.Sleep(delayMs);
                    }
                }
            }
            catch
            {
                _writer = null;
                FileWriteError = true;
            }
        }

        protected override void Write(LogEvent evt)
        {
            if (_writer == null || FileWriteError) return;

            try
            {
                _writer.Write(Format(evt));
                _writer.Flush();
            }
            catch
            {
                FileWriteError = true;
                try { _writer.Close(); } catch { }
                _writer = null;
            }
        }

        protected override void WriteDroppedMarker(long count)
        {
            if (_writer == null || FileWriteError) return;
            try
            {
                _writer.Write(string.Concat(
                    DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    "  WARN   [Log]    ", count.ToString(), " log event(s) dropped under load",
                    Environment.NewLine));
                _writer.Flush();
            }
            catch { FileWriteError = true; }
        }

        private static string Format(LogEvent evt)
        {
            StringBuilder sb = new StringBuilder(128);
            sb.Append(evt.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            sb.Append("  ");
            sb.Append(LevelTag(evt.Level));
            sb.Append(' ');

            // Source tag comes from the SCOPE (e.g. "[In]", "[Core]", "[Wii]") — classification is
            // scope + level only; there is no category axis. Cross-cutting lines set a "prefix"
            // scope property (see ScopePrefix) so they render their tag here.
            string prefix = ScopePrefix.Resolve(evt.Scope);
            // Skip the bare root scope ("NKit") — not a useful tag on dividers/uncategorised lines.
            if (!string.IsNullOrEmpty(prefix) && prefix != "NKit")
                sb.Append('[').Append(prefix).Append("] ");

            sb.Append(evt.Message);

            if (evt.Exception != null)
            {
                sb.Append(Environment.NewLine);
                sb.Append(evt.Exception.ToString());
            }

            sb.Append(Environment.NewLine);
            return sb.ToString();
        }

        private static string LevelTag(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Error: return "ERROR ";
                case LogLevel.Warning: return "WARN  ";
                case LogLevel.Info: return "INFO  ";
                case LogLevel.Detail: return "DETAIL";
                case LogLevel.Trace: return "TRACE ";
                default: return "      ";
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            if (_writer != null)
            {
                try { _writer.Close(); } catch { }
                _writer = null;
            }
        }
    }
}