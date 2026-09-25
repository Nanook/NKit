using Avalonia.Threading;
using NKDS;
using NKDS.Models;
using NkdsUi.ViewModels;
using NKitDataStore;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.Services;

/// <summary>
/// Processes command-line arguments for the application.
/// Handles opening DataStores, performing add/adddir operations,
/// and accumulating paths from multiple remote instances.
/// </summary>
public sealed class CommandLineProcessor
{
    private readonly ServiceRegistry _serviceRegistry;
    private readonly Func<MainWindowViewModel?> _viewModelAccessor;

    private readonly List<string> _pendingInputPaths = new();
    private readonly object _pendingLock = new();
    private bool _isProcessingAdd;

    /// <summary>
    /// Creates a new CommandLineProcessor.
    /// </summary>
    /// <param name="serviceRegistry">The application service registry.</param>
    /// <param name="viewModelAccessor">
    /// A function that returns the current MainWindowViewModel (may be null during startup).
    /// </param>
    public CommandLineProcessor(ServiceRegistry serviceRegistry, Func<MainWindowViewModel?> viewModelAccessor)
    {
        _serviceRegistry = serviceRegistry;
        _viewModelAccessor = viewModelAccessor;
    }

    /// <summary>
    /// Called from the pipe server when a subsequent instance sends its args.
    /// Accumulates input paths for batch processing, or starts a new operation
    /// if no add is currently in progress.
    /// </summary>
    public async Task ProcessRemoteArgsAsync(string[] args)
    {
        List<string> inputPaths = new List<string>();
        string? action = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--input" && i + 1 < args.Length)
            {
                inputPaths.Add(args[i + 1]);
                i++;
            }
            else if (arg == "--action" && i + 1 < args.Length)
            {
                action = args[i + 1];
                i++;
            }
            else if (arg.StartsWith("--") && i + 1 < args.Length)
            {
                i++; // skip other flag values
            }
            else if (!arg.StartsWith("--") && !arg.StartsWith("-"))
            {
                inputPaths.Add(arg);
            }
        }

        if (inputPaths.Count == 0 && action == null) return;

        // If an add operation is already in progress, just accumulate paths
        if (_isProcessingAdd)
        {
            lock (_pendingLock)
            {
                foreach (string p in inputPaths)
                    _pendingInputPaths.Add(p);
            }
            return;
        }

        // No operation in progress — start a new one with the full args
        await ProcessArgsAsync(args);
    }

    /// <summary>
    /// Processes command-line arguments to open a DataStore directory or .nkds file,
    /// or perform add/adddir operations.
    /// Supports: --datastore &lt;path&gt;, --set &lt;path&gt;, --input &lt;path&gt;,
    /// --action mount|add|adddir|addnew|adddirnew
    /// </summary>
    public async Task ProcessArgsAsync(string[] args)
    {
        MainWindowViewModel? viewModel = _viewModelAccessor();
        if (viewModel == null) return;

        string? path = null;
        List<string> inputPaths = new List<string>();
        bool isFile = false;
        string? action = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if ((arg == "--datastore" || arg == "-ds") && i + 1 < args.Length)
            {
                path = args[i + 1];
                isFile = path.EndsWith(".nkds", StringComparison.OrdinalIgnoreCase);
                i++;
            }
            else if (arg == "--set" && i + 1 < args.Length)
            {
                path = args[i + 1];
                isFile = true;
                i++;
            }
            else if (arg == "--input" && i + 1 < args.Length)
            {
                inputPaths.Add(args[i + 1]);
                i++;
            }
            else if (arg == "--action" && i + 1 < args.Length)
            {
                action = args[i + 1];
                i++;
            }
            else if (!arg.StartsWith("--") && !arg.StartsWith("-"))
            {
                // Treat positional arguments as additional input paths
                inputPaths.Add(arg);
            }
        }

        try
        {
            if (string.Equals(action, "add", StringComparison.OrdinalIgnoreCase) && inputPaths.Count > 0)
            {
                await HandleAddActionAsync(viewModel, inputPaths);
                return;
            }

            if (string.Equals(action, "adddir", StringComparison.OrdinalIgnoreCase) && inputPaths.Count > 0)
            {
                await HandleAddDirActionAsync(viewModel, inputPaths);
                return;
            }

            if (string.Equals(action, "addnew", StringComparison.OrdinalIgnoreCase) && inputPaths.Count > 0)
            {
                await HandleAddNewActionAsync(viewModel, inputPaths);
                return;
            }

            if (string.Equals(action, "adddirnew", StringComparison.OrdinalIgnoreCase) && inputPaths.Count > 0)
            {
                await HandleAddDirNewActionAsync(viewModel, inputPaths);
                return;
            }

            // Standard open flow
            if (string.IsNullOrEmpty(path)) return;

            if (isFile && File.Exists(path))
            {
                await viewModel.SessionManager.OpenFileAsync(path);
                _serviceRegistry.ConfigService.AddDataStorePath(path);
            }
            else if (Directory.Exists(path))
            {
                await viewModel.SessionManager.OpenDirectoryAsync(path);
                _serviceRegistry.ConfigService.AddDataStorePath(path);
            }
            else
            {
                return;
            }

            // Allow the session to fully initialize before triggering actions
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            // Handle post-open actions
            if (string.Equals(action, "mount", StringComparison.OrdinalIgnoreCase))
            {
                viewModel.Toolbar.MountCommand.Execute().Subscribe();
            }
        }
        catch (Exception ex)
        {
            _serviceRegistry.ErrorNotificationService?.PublishOperationError(
                $"Failed to process command line: {ex.Message}");
        }
    }

    /// <summary>
    /// Accumulates input paths from remote instances into the pending collection.
    /// Used by ProcessRemoteArgsAsync when an add operation is already in progress.
    /// </summary>
    public void AccumulatePaths(string[] args)
    {
        lock (_pendingLock)
        {
            foreach (string arg in args)
            {
                if (!arg.StartsWith("--") && !arg.StartsWith("-"))
                    _pendingInputPaths.Add(arg);
            }
        }
    }

    /// <summary>
    /// Collects and clears all pending input paths accumulated from remote instances.
    /// </summary>
    private List<string> CollectPendingInputPaths()
    {
        lock (_pendingLock)
        {
            List<string> paths = new List<string>(_pendingInputPaths);
            _pendingInputPaths.Clear();
            return paths;
        }
    }

    /// <summary>
    /// Handles --action add: shows a file picker to select a .nkds set, opens it,
    /// then directly adds the specified input files to that set.
    /// </summary>
    private async Task HandleAddActionAsync(MainWindowViewModel viewModel, List<string> inputPaths)
    {
        _isProcessingAdd = true;
        try
        {
            // Brief delay to allow other instances (from multi-select) to send their paths
            await Task.Delay(300);

            // Collect any paths that arrived from other instances during the delay
            List<string> additionalPaths = CollectPendingInputPaths();
            foreach (string p in additionalPaths)
            {
                if (!inputPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                    inputPaths.Add(p);
            }

            // Show file picker to select the target .nkds set
            string setPath = await viewModel.ShowFilePicker.Handle(RxVoid.Default);
            if (string.IsNullOrEmpty(setPath)) return;

            // Open the selected set
            await viewModel.SessionManager.OpenFileAsync(setPath);
            _serviceRegistry.ConfigService.AddDataStorePath(setPath);

            // Wait for the session to fully initialize
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(500);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            // Collect any additional paths that arrived while the picker was open
            additionalPaths = CollectPendingInputPaths();
            foreach (string p in additionalPaths)
            {
                if (!inputPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                    inputPaths.Add(p);
            }

            // Perform the add using the full infrastructure (progress, sync, exclusive access)
            await viewModel.ExecuteAddWithInputAsync(inputPaths);
        }
        finally
        {
            _isProcessingAdd = false;
        }
    }

    /// <summary>
    /// Handles --action adddir: shows a file picker to select a .nkds set, opens it,
    /// then directly adds the specified input folders to that set.
    /// </summary>
    private async Task HandleAddDirActionAsync(MainWindowViewModel viewModel, List<string> inputPaths)
    {
        _isProcessingAdd = true;
        try
        {
            // Brief delay to allow other instances (from multi-select) to send their paths
            await Task.Delay(300);

            // Collect any paths that arrived from other instances during the delay
            List<string> additionalPaths = CollectPendingInputPaths();
            foreach (string p in additionalPaths)
            {
                if (!inputPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                    inputPaths.Add(p);
            }

            // Show file picker to select the target .nkds set
            string setPath = await viewModel.ShowFilePicker.Handle(RxVoid.Default);
            if (string.IsNullOrEmpty(setPath)) return;

            // Open the selected set
            await viewModel.SessionManager.OpenFileAsync(setPath);
            _serviceRegistry.ConfigService.AddDataStorePath(setPath);

            // Wait for the session to fully initialize
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(500);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            // Collect any additional paths that arrived while the picker was open
            additionalPaths = CollectPendingInputPaths();
            foreach (string p in additionalPaths)
            {
                if (!inputPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                    inputPaths.Add(p);
            }

            // Perform the adddir using the full infrastructure (progress, sync, exclusive access)
            await viewModel.ExecuteAddDirWithInputAsync(inputPaths);
        }
        finally
        {
            _isProcessingAdd = false;
        }
    }

    /// <summary>
    /// Handles --action addnew: shows folder picker, then Create Set dialog,
    /// creates the set, opens it, then adds the input files.
    /// </summary>
    private async Task HandleAddNewActionAsync(MainWindowViewModel viewModel, List<string> inputPaths)
    {
        _isProcessingAdd = true;
        try
        {
            // Brief delay to allow other instances to send their paths
            await Task.Delay(300);
            List<string> additionalPaths = CollectPendingInputPaths();
            foreach (string p in additionalPaths)
            {
                if (!inputPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                    inputPaths.Add(p);
            }

            // Show folder picker to choose where to create the set
            string folderPath = await viewModel.ShowFolderPicker.Handle(RxVoid.Default);
            if (string.IsNullOrEmpty(folderPath)) return;

            // Show Create Set dialog
            CreateSetDialogViewModel dialogVm = new CreateSetDialogViewModel(folderPath, _serviceRegistry.ConfigService);
            bool confirmed = await viewModel.ShowCreateSetDialog.Handle(dialogVm);
            if (!confirmed) return;

            // Create the set
            using (NkdsOperations ops = new NKDS.NkdsOperations())
            {
                OperationResult result = ops.CreateSet(folderPath, dialogVm.SetName, dialogVm.ParsedShardSize, dialogVm.ParsedBlockSize);
                if (!result.Success)
                {
                    _serviceRegistry.ErrorNotificationService?.PublishOperationError(
                        $"Create Set failed: {result.Errors?.FirstOrDefault()?.Reason ?? "Unknown"}");
                    return;
                }
            }

            // Open the new set
            string setFilePath = Path.Combine(folderPath, dialogVm.SetName + DataStore.DatabaseFileExtension);
            await viewModel.SessionManager.OpenFileAsync(setFilePath);
            _serviceRegistry.ConfigService.AddDataStorePath(setFilePath);

            // Wait for session to initialize
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(500);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            // Collect any additional paths that arrived while dialogs were open
            additionalPaths = CollectPendingInputPaths();
            foreach (string p in additionalPaths)
            {
                if (!inputPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                    inputPaths.Add(p);
            }

            // Add the files to the new set
            await viewModel.ExecuteAddWithInputAsync(inputPaths);
        }
        finally
        {
            _isProcessingAdd = false;
        }
    }

    /// <summary>
    /// Handles --action adddirnew: shows folder picker, then Create Set dialog,
    /// creates the set, opens it, then adds the input folders.
    /// </summary>
    private async Task HandleAddDirNewActionAsync(MainWindowViewModel viewModel, List<string> inputPaths)
    {
        _isProcessingAdd = true;
        try
        {
            // Brief delay to allow other instances to send their paths
            await Task.Delay(300);
            List<string> additionalPaths = CollectPendingInputPaths();
            foreach (string p in additionalPaths)
            {
                if (!inputPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                    inputPaths.Add(p);
            }

            // Show folder picker to choose where to create the set
            string folderPath = await viewModel.ShowFolderPicker.Handle(RxVoid.Default);
            if (string.IsNullOrEmpty(folderPath)) return;

            // Show Create Set dialog
            CreateSetDialogViewModel dialogVm = new CreateSetDialogViewModel(folderPath, _serviceRegistry.ConfigService);
            bool confirmed = await viewModel.ShowCreateSetDialog.Handle(dialogVm);
            if (!confirmed) return;

            // Create the set
            using (NkdsOperations ops = new NKDS.NkdsOperations())
            {
                OperationResult result = ops.CreateSet(folderPath, dialogVm.SetName, dialogVm.ParsedShardSize, dialogVm.ParsedBlockSize);
                if (!result.Success)
                {
                    _serviceRegistry.ErrorNotificationService?.PublishOperationError(
                        $"Create Set failed: {result.Errors?.FirstOrDefault()?.Reason ?? "Unknown"}");
                    return;
                }
            }

            // Open the new set
            string setFilePath = Path.Combine(folderPath, dialogVm.SetName + DataStore.DatabaseFileExtension);
            await viewModel.SessionManager.OpenFileAsync(setFilePath);
            _serviceRegistry.ConfigService.AddDataStorePath(setFilePath);

            // Wait for session to initialize
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(500);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            // Collect any additional paths that arrived while dialogs were open
            additionalPaths = CollectPendingInputPaths();
            foreach (string p in additionalPaths)
            {
                if (!inputPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                    inputPaths.Add(p);
            }

            // Add the folders to the new set
            await viewModel.ExecuteAddDirWithInputAsync(inputPaths);
        }
        finally
        {
            _isProcessingAdd = false;
        }
    }
}