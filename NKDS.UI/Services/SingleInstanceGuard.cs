using System.IO.Pipes;
using System.Text;

namespace NkdsUi.Services;

/// <summary>
/// Manages single-instance enforcement via a named mutex and named-pipe IPC.
/// The first instance acquires the mutex and starts a pipe server to receive
/// arguments from subsequent instances. Later instances detect the mutex,
/// forward their arguments via the pipe, and exit.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "NkdsUi_SingleInstance_Mutex";
    private const string PipeName = "NkdsUi_SingleInstance_Pipe";

    private Mutex? _mutex;
    private Thread? _serverThread;
    private volatile bool _disposed;

    /// <summary>
    /// Attempts to acquire the single-instance mutex.
    /// Returns true if this is the first (owning) instance.
    /// </summary>
    public bool IsFirstInstance()
    {
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        return createdNew;
    }

    /// <summary>
    /// Sends command-line arguments to the already-running instance via named pipe.
    /// Retries connection to handle the race where the pipe server hasn't started yet.
    /// </summary>
    public void SendArgsToRunningInstance(string[] args)
    {
        if (args.Length == 0) return;

        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using NamedPipeClientStream client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(2000); // 2 second timeout per attempt

                using StreamWriter writer = new StreamWriter(client, Encoding.UTF8);
                writer.WriteLine(args.Length.ToString());
                foreach (string arg in args)
                    writer.WriteLine(arg);
                writer.Flush();
                return; // Success
            }
            catch
            {
                // Pipe not ready yet — wait and retry
                Thread.Sleep(200);
            }
        }
    }

    /// <summary>
    /// Starts a background pipe server that listens for args from subsequent instances.
    /// When args arrive, <paramref name="onArgsReceived"/> is invoked (on the pipe thread).
    /// </summary>
    public void StartListening(Action<string[]> onArgsReceived)
    {
        _serverThread = new Thread(() => PipeServerLoop(onArgsReceived))
        {
            IsBackground = true,
            Name = "SingleInstancePipeServer"
        };
        _serverThread.Start();
    }

    private void PipeServerLoop(Action<string[]> onArgsReceived)
    {
        while (!_disposed)
        {
            try
            {
                using NamedPipeServerStream server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                server.WaitForConnection();

                using StreamReader reader = new StreamReader(server, Encoding.UTF8);
                string? countLine = reader.ReadLine();
                if (countLine == null || !int.TryParse(countLine, out int count) || count <= 0)
                    continue;

                string[] args = new string[count];
                for (int i = 0; i < count; i++)
                    args[i] = reader.ReadLine() ?? "";

                onArgsReceived(args);
            }
            catch
            {
                if (_disposed) break;
                // If pipe breaks, wait a bit and retry
                Thread.Sleep(100);
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _mutex?.Dispose();
        _mutex = null;
    }
}