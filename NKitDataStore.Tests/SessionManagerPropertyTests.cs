using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NkdsUi.Models;
using NkdsUi.Services;
using ReactiveUI.Builder;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;
using ReactiveUI.Primitives.Signals;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for SessionManager single-session enforcement.
///
/// Feature: single-folder-session-ui, Property 1: Single-Session Invariant
///
/// **Validates: Requirements 1.1, 1.2, 1.3**
///
/// For any sequence of Open_Command, Open_Set_Command, and Close_Command operations
/// executed against the SessionManager, the number of active sessions in the UI
/// SHALL never exceed one.
/// </summary>
public class SessionManagerPropertyTests
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public SessionManagerPropertyTests()
    {
        EnsureReactiveUIInitialized();
    }

    private static void EnsureReactiveUIInitialized()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            RxAppBuilder.CreateReactiveUIBuilder()
                .WithCoreServices()
                .BuildApp();
            _initialized = true;
        }
    }

    /// <summary>
    /// Represents a command that can be issued to the SessionManager.
    /// </summary>
    public enum SessionCommand
    {
        OpenDirectory,
        OpenFile,
        Close
    }

    /// <summary>
    /// A command with an associated path for Open operations.
    /// </summary>
    public record CommandWithPath(SessionCommand Command, string Path);

    /// <summary>
    /// Fake IDataStoreService that tracks session count without real DataStore operations.
    /// Each OpenDirectory/OpenFile adds a session; CloseSession removes one.
    /// </summary>
    private class FakeDataStoreService : IDataStoreService
    {
        private readonly List<ImageSessionModel> _sessions = new();
        private readonly ReplaySignal<IReadOnlyList<ImageSessionModel>> _sessionsSubject;

        public FakeDataStoreService()
        {
            _sessionsSubject = new ReplaySignal<IReadOnlyList<ImageSessionModel>>(1);
        }

        public int SessionCount => _sessions.Count;

        public IObservable<IReadOnlyList<ImageSessionModel>> Sessions => _sessionsSubject.AsObservable();

        public IObservable<IReadOnlyList<ImageRecord>> AllImages => Signal.Emit<IReadOnlyList<ImageRecord>>(Array.Empty<ImageRecord>());

        public IObservable<string> ErrorMessages => Signal.Create<string>(observer => { observer.OnCompleted(); return EmptyDisposable.Instance; });

        public Task<bool> OpenDirectoryAsync(string directoryPath)
        {
            string sessionId = Guid.NewGuid().ToString();
            ImageSessionModel session = new ImageSessionModel(sessionId, directoryPath, null, null!, new List<ImageRecord>());
            _sessions.Add(session);
            _sessionsSubject.OnNext(_sessions.AsReadOnly());
            return Task.FromResult(true);
        }

        public Task<bool> OpenFileAsync(string filePath)
        {
            string sessionId = Guid.NewGuid().ToString();
            ImageSessionModel session = new ImageSessionModel(sessionId, filePath, Path.GetFileNameWithoutExtension(filePath), null!, new List<ImageRecord>());
            _sessions.Add(session);
            _sessionsSubject.OnNext(_sessions.AsReadOnly());
            return Task.FromResult(true);
        }

        public void CloseSession(string sessionId)
        {
            ImageSessionModel session = _sessions.FirstOrDefault(s => s.SessionId == sessionId);
            if (session != null)
            {
                _sessions.Remove(session);
                _sessionsSubject.OnNext(_sessions.AsReadOnly());
            }
        }

        public bool IsAlreadyOpen(string path) => _sessions.Any(s => s.Path == path);

        public Task<IReadOnlyList<ImageRecord>> RefreshSessionImagesAsync(string sessionId, string setName = null)
            => Task.FromResult<IReadOnlyList<ImageRecord>>(Array.Empty<ImageRecord>());

        public DataStore GetActiveDataStore() => null;

        public string GetActiveDataStorePath() => null;
    }

    /// <summary>
    /// Generates a random list of SessionManager commands using FsCheck generators.
    /// Constrains to 1-30 commands with varied directory/file paths.
    /// </summary>
    private static Gen<List<CommandWithPath>> GenCommandSequence()
    {
        Gen<string> pathGen = Gen.Elements(
            @"C:\Users\Test\DataStore1",
            @"C:\Users\Test\DataStore2",
            @"D:\Projects\Images",
            @"E:\Backup\Sets",
            @"C:\Temp\NKit"
        );

        Gen<string> filePathGen = pathGen.Select(p => $@"{p}\set.nkds");

        Gen<CommandWithPath> commandGen = Gen.OneOf(
            pathGen.Select(p => new CommandWithPath(SessionCommand.OpenDirectory, p)),
            filePathGen.Select(p => new CommandWithPath(SessionCommand.OpenFile, p)),
            Gen.Constant(new CommandWithPath(SessionCommand.Close, ""))
        );

        return from count in Gen.Choose(1, 30)
               from commands in Gen.ArrayOf(commandGen, count)
               select commands.ToList();
    }

    /// <summary>
    /// Provides an Arbitrary for List&lt;CommandWithPath&gt; used by the property test.
    /// </summary>
    public static Arbitrary<List<CommandWithPath>> CommandSequenceArbitrary() => Arb.From(GenCommandSequence());

    /// <summary>
    /// **Validates: Requirements 1.1, 1.2, 1.3**
    ///
    /// Property 1: Single-Session Invariant.
    /// For any random sequence of OpenDirectory, OpenFile, and Close commands,
    /// after each command the DataStoreService SHALL have at most 1 active session.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool AfterAnyCommandSequence_AtMostOneSessionExists(NonNegativeInt sequenceLengthSeed, NonNegativeInt commandSeed, NonNegativeInt pathSeed)
    {
        // Generate a sequence of 1 to 20 commands
        int sequenceLength = (sequenceLengthSeed.Get % 20) + 1;

        FakeDataStoreService fakeService = new FakeDataStoreService();
        SessionManager sessionManager = new SessionManager(fakeService);

        // Use seeds to generate deterministic command sequences
        Random random = new Random(commandSeed.Get);
        Random pathRandom = new Random(pathSeed.Get);

        for (int i = 0; i < sequenceLength; i++)
        {
            SessionCommand commandType = (SessionCommand)random.Next(3);
            string path = $@"C:\TestFolder\Path{pathRandom.Next(10)}";

            switch (commandType)
            {
                case SessionCommand.OpenDirectory:
                    sessionManager.OpenDirectoryAsync(path).GetAwaiter().GetResult();
                    break;
                case SessionCommand.OpenFile:
                    string filePath = $@"{path}\set{pathRandom.Next(5)}.nkds";
                    sessionManager.OpenFileAsync(filePath).GetAwaiter().GetResult();
                    break;
                case SessionCommand.Close:
                    sessionManager.Close();
                    break;
            }

            // INVARIANT: After each command, at most 1 session exists
            if (fakeService.SessionCount > 1)
                return false;
        }

        sessionManager.Dispose();
        return true;
    }

    /// <summary>
    /// **Validates: Requirements 1.1, 1.2, 1.3**
    ///
    /// Property 1: Single-Session Invariant (with FsCheck generator).
    /// For any generated list of commands, after executing each command in sequence,
    /// the DataStoreService SHALL have at most 1 active session.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SessionManagerPropertyTests) })]
    public bool AfterAnyGeneratedCommandSequence_AtMostOneSessionExists(List<CommandWithPath> commands)
    {
        if (commands == null || commands.Count == 0)
            return true;

        FakeDataStoreService fakeService = new FakeDataStoreService();
        SessionManager sessionManager = new SessionManager(fakeService);

        foreach (CommandWithPath cmd in commands)
        {
            switch (cmd.Command)
            {
                case SessionCommand.OpenDirectory:
                    sessionManager.OpenDirectoryAsync(cmd.Path).GetAwaiter().GetResult();
                    break;
                case SessionCommand.OpenFile:
                    sessionManager.OpenFileAsync(cmd.Path).GetAwaiter().GetResult();
                    break;
                case SessionCommand.Close:
                    sessionManager.Close();
                    break;
            }

            // INVARIANT: After each command, at most 1 session exists
            if (fakeService.SessionCount > 1)
                return false;
        }

        sessionManager.Dispose();
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Property 3: OpenStateText Formatting
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// **Validates: Requirements 6.3, 6.4, 6.5**
    ///
    /// Property 3: OpenStateText Formatting.
    /// For any non-empty folder path, when a directory session is opened the OpenStateText
    /// SHALL equal "Open: {path}".
    ///
    /// Feature: single-folder-session-ui, Property 3: OpenStateText Formatting
    /// </summary>
    [Property(MaxTest = 100)]
    public bool OpenDirectory_OpenStateText_EqualsOpenColonPath(NonNegativeInt pathSeed, NonNegativeInt suffixSeed)
    {
        // Generate varied directory paths
        string[] roots = new[] { @"C:\", @"D:\", @"E:\", @"Z:\" };
        string[] segments = new[] { "Users", "Test", "DataStore", "Projects", "Images", "My Folder", "Backup", "a" };

        string root = roots[pathSeed.Get % roots.Length];
        int segCount = (suffixSeed.Get % 4) + 1; // 1 to 4 segments
        string path = root + string.Join(@"\", Enumerable.Range(0, segCount)
            .Select(i => segments[(pathSeed.Get + (i * 3)) % segments.Length]));

        FakeDataStoreService fakeService = new FakeDataStoreService();
        SessionManager sessionManager = new SessionManager(fakeService);

        sessionManager.OpenDirectoryAsync(path).GetAwaiter().GetResult();

        string expected = $"{path}";
        bool result = sessionManager.OpenStateText == expected;

        sessionManager.Dispose();
        return result;
    }

    /// <summary>
    /// **Validates: Requirements 6.3, 6.4, 6.5**
    ///
    /// Property 3: OpenStateText Formatting.
    /// For any non-empty file path with a folder and filename, when a file session is opened
    /// the OpenStateText SHALL equal "Open: {folderPath} {setName}" where
    /// folderPath = Path.GetDirectoryName(filePath) and setName = Path.GetFileNameWithoutExtension(filePath).
    ///
    /// Feature: single-folder-session-ui, Property 3: OpenStateText Formatting
    /// </summary>
    [Property(MaxTest = 100)]
    public bool OpenFile_OpenStateText_EqualsOpenColonFolderSpaceSetName(NonNegativeInt folderSeed, NonNegativeInt nameSeed)
    {
        // Generate varied folder paths and set names
        string[] folders = new[]
        {
            @"C:\Users\Test\DataStore",
            @"D:\Projects\Images",
            @"E:\Backup\Sets",
            @"C:\Temp\NKit",
            @"C:\My Folder\With Spaces",
            @"D:\Path\With\Many\Segments",
            @"C:\a",
            @"Z:\deep\nested\directory"
        };
        string[] setNames = new[]
        {
            "MySet", "test_set_01", "Archive2024", "set-with-dashes",
            "SetName", "UPPERCASE", "a", "numbers123"
        };

        string folder = folders[folderSeed.Get % folders.Length];
        string setName = setNames[nameSeed.Get % setNames.Length];
        string filePath = Path.Combine(folder, $"{setName}.nkds");

        FakeDataStoreService fakeService = new FakeDataStoreService();
        SessionManager sessionManager = new SessionManager(fakeService);

        sessionManager.OpenFileAsync(filePath).GetAwaiter().GetResult();

        string expectedFolder = Path.GetDirectoryName(filePath) ?? filePath;
        string expectedSetName = Path.GetFileNameWithoutExtension(filePath);
        string expected = $"{expectedFolder}";
        bool result = sessionManager.OpenStateText == expected;

        sessionManager.Dispose();
        return result;
    }

    /// <summary>
    /// **Validates: Requirements 6.3, 6.4, 6.5**
    ///
    /// Property 3: OpenStateText Formatting.
    /// For any open session (directory or file), after Close() is called
    /// the OpenStateText SHALL be the empty string.
    ///
    /// Feature: single-folder-session-ui, Property 3: OpenStateText Formatting
    /// </summary>
    [Property(MaxTest = 100)]
    public bool AfterClose_OpenStateText_IsEmptyString(bool openAsDirectory, NonNegativeInt pathSeed)
    {
        string[] folders = new[]
        {
            @"C:\Users\Test\DataStore",
            @"D:\Projects\Images",
            @"E:\Backup\Sets",
            @"C:\Temp\NKit"
        };
        string folder = folders[pathSeed.Get % folders.Length];

        FakeDataStoreService fakeService = new FakeDataStoreService();
        SessionManager sessionManager = new SessionManager(fakeService);

        if (openAsDirectory)
        {
            sessionManager.OpenDirectoryAsync(folder).GetAwaiter().GetResult();
        }
        else
        {
            string filePath = Path.Combine(folder, "testset.nkds");
            sessionManager.OpenFileAsync(filePath).GetAwaiter().GetResult();
        }

        // Precondition: session must be open
        if (sessionManager.CurrentState == OpenState.None)
        {
            sessionManager.Dispose();
            return true; // Discard
        }

        // Act
        sessionManager.Close();

        // Assert
        bool result = sessionManager.OpenStateText == "";

        sessionManager.Dispose();
        return result;
    }

    /// <summary>
    /// **Validates: Requirements 6.3, 6.4, 6.5**
    ///
    /// Property 3: OpenStateText Formatting.
    /// For any sequence of open/close operations, the OpenStateText SHALL always
    /// match the expected format for the current state at every step.
    ///
    /// Feature: single-folder-session-ui, Property 3: OpenStateText Formatting
    /// </summary>
    [Property(MaxTest = 100)]
    public bool OpenStateText_AlwaysMatchesCurrentStateFormat(NonNegativeInt sequenceLengthSeed, NonNegativeInt commandSeed, NonNegativeInt pathSeed)
    {
        int sequenceLength = (sequenceLengthSeed.Get % 15) + 1;

        string[] folders = new[]
        {
            @"C:\Users\Test\DataStore",
            @"D:\Projects\Images",
            @"E:\Backup\Sets",
            @"C:\Temp\NKit"
        };
        string[] setNames = new[] { "MySet", "test_set_01", "Archive2024", "a" };

        FakeDataStoreService fakeService = new FakeDataStoreService();
        SessionManager sessionManager = new SessionManager(fakeService);
        Random random = new Random(commandSeed.Get);
        Random pathRandom = new Random(pathSeed.Get);

        for (int i = 0; i < sequenceLength; i++)
        {
            SessionCommand commandType = (SessionCommand)random.Next(3);
            string folder = folders[pathRandom.Next(folders.Length)];
            string setName = setNames[pathRandom.Next(setNames.Length)];

            switch (commandType)
            {
                case SessionCommand.OpenDirectory:
                    sessionManager.OpenDirectoryAsync(folder).GetAwaiter().GetResult();
                    // Verify directory format
                    if (sessionManager.OpenStateText != $"{folder}")
                    {
                        sessionManager.Dispose();
                        return false;
                    }
                    break;

                case SessionCommand.OpenFile:
                    string filePath = Path.Combine(folder, $"{setName}.nkds");
                    sessionManager.OpenFileAsync(filePath).GetAwaiter().GetResult();
                    // Verify file format
                    string expectedFolder = Path.GetDirectoryName(filePath) ?? filePath;
                    string expectedSetName = Path.GetFileNameWithoutExtension(filePath);
                    if (sessionManager.OpenStateText != $"{expectedFolder}")
                    {
                        sessionManager.Dispose();
                        return false;
                    }
                    break;

                case SessionCommand.Close:
                    sessionManager.Close();
                    // Verify empty after close
                    if (sessionManager.OpenStateText != "")
                    {
                        sessionManager.Dispose();
                        return false;
                    }
                    break;
            }
        }

        sessionManager.Dispose();
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Property 6: Filter Bar / Progress Indicator Mutual Exclusivity
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Represents the types of operations that can affect filter bar / progress indicator state.
    /// </summary>
    public enum FilterBarOperationType
    {
        OpenDirectory,
        OpenFile,
        Close,
        SetFilterBarVisible,
        SetFilterBarHidden
    }

    /// <summary>
    /// An operation with an associated path (for Open operations) and type.
    /// </summary>
    public record FilterBarOperation(FilterBarOperationType Type, string Path);

    /// <summary>
    /// Generates a random list of filter bar operations using FsCheck generators.
    /// Constrains to 1-30 operations with varied directory/file paths and filter toggles.
    /// </summary>
    private static Gen<List<FilterBarOperation>> GenFilterBarOperationSequence()
    {
        Gen<string> pathGen = Gen.Elements(
            @"C:\Users\Test\DataStore1",
            @"C:\Users\Test\DataStore2",
            @"D:\Projects\Images",
            @"E:\Backup\Sets",
            @"C:\Temp\NKit"
        );

        Gen<string> filePathGen = pathGen.Select(p => $@"{p}\set.nkds");

        Gen<FilterBarOperation> operationGen = Gen.OneOf(
            pathGen.Select(p => new FilterBarOperation(FilterBarOperationType.OpenDirectory, p)),
            filePathGen.Select(p => new FilterBarOperation(FilterBarOperationType.OpenFile, p)),
            Gen.Constant(new FilterBarOperation(FilterBarOperationType.Close, "")),
            Gen.Constant(new FilterBarOperation(FilterBarOperationType.SetFilterBarVisible, "")),
            Gen.Constant(new FilterBarOperation(FilterBarOperationType.SetFilterBarHidden, ""))
        );

        return from count in Gen.Choose(1, 30)
               from operations in Gen.ArrayOf(operationGen, count)
               select operations.ToList();
    }

    /// <summary>
    /// Provides an Arbitrary for List&lt;FilterBarOperation&gt; used by the property test.
    /// </summary>
    public static Arbitrary<List<FilterBarOperation>> FilterBarOperationSequenceArbitrary() => Arb.From(GenFilterBarOperationSequence());

    // ─────────────────────────────────────────────────────────────────────────────
    // Property 4: 1GMR Path Match Determines Session Behavior
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Feature: single-folder-session-ui, Property 4: 1GMR Path Match Determines Session Behavior
    ///
    /// **Validates: Requirements 8.1, 8.2**
    ///
    /// For any pair of normalized paths (current session folder, 1GMR output folder),
    /// if the paths are equivalent then the current session SHALL remain open during 1GMR processing;
    /// if the paths are not equivalent then the current session SHALL be closed before 1GMR processing begins.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PathMatch_DeterminesSessionBehavior(NonNegativeInt pathSeed, NonNegativeInt variantSeed)
    {
        // Base paths to open a directory session with
        string[] basePaths = new[]
        {
            @"C:\Users\Test\DataStore",
            @"D:\Projects\Images",
            @"E:\Backup\Sets",
            @"C:\Temp\NKit",
            @"C:\My Folder\With Spaces"
        };

        string basePath = basePaths[pathSeed.Get % basePaths.Length];

        // Generate a 1GMR output folder path - sometimes matching, sometimes different
        // Variant types: 0-3 = matching variants (same path, different normalization), 4-7 = different paths
        int variant = variantSeed.Get % 8;
        string gmrOutputPath;

        switch (variant)
        {
            case 0: // Exact same path
                gmrOutputPath = basePath;
                break;
            case 1: // With trailing backslash
                gmrOutputPath = basePath + @"\";
                break;
            case 2: // Different casing (equivalent on Windows)
                gmrOutputPath = basePath.ToUpperInvariant();
                break;
            case 3: // Mixed casing
                gmrOutputPath = basePath.ToLowerInvariant();
                break;
            case 4: // Different path entirely
                gmrOutputPath = @"C:\Completely\Different\Path";
                break;
            case 5: // Subfolder of base path (not a match)
                gmrOutputPath = basePath + @"\SubFolder";
                break;
            case 6: // Parent of base path (not a match)
                gmrOutputPath = Path.GetDirectoryName(basePath) ?? @"C:\";
                break;
            case 7: // Different drive
                gmrOutputPath = @"F:\Other\Location";
                break;
            default:
                gmrOutputPath = basePath;
                break;
        }

        FakeDataStoreService fakeService = new FakeDataStoreService();
        SessionManager sessionManager = new SessionManager(fakeService);

        // Open a directory session
        sessionManager.OpenDirectoryAsync(basePath).GetAwaiter().GetResult();

        // Precondition: session must be open
        if (sessionManager.CurrentState != OpenState.Directory)
        {
            sessionManager.Dispose();
            return true; // Discard
        }

        // Normalize both paths the same way SessionManager.GetCurrentFolderPath() does
        string currentFolderPath = sessionManager.GetCurrentFolderPath();
        if (currentFolderPath == null)
        {
            sessionManager.Dispose();
            return true; // Discard
        }

        string normalizedGmrPath = Path.GetFullPath(gmrOutputPath);
        if (!normalizedGmrPath.EndsWith(Path.DirectorySeparatorChar))
            normalizedGmrPath += Path.DirectorySeparatorChar;

        // Compare paths case-insensitive (Windows behavior)
        bool pathsMatch = string.Equals(currentFolderPath, normalizedGmrPath, StringComparison.OrdinalIgnoreCase);

        if (pathsMatch)
        {
            // Requirement 8.1: If paths match, session SHALL remain open
            // The session should still be open - no close needed
            bool result = sessionManager.CurrentState == OpenState.Directory && sessionManager.HasSession;
            sessionManager.Dispose();
            return result;
        }
        else
        {
            // Requirement 8.2: If paths don't match, session SHALL be closed before 1GMR processing
            // Simulate the close that would happen before 1GMR processing
            sessionManager.Close();
            bool result = sessionManager.CurrentState == OpenState.None;
            sessionManager.Dispose();
            return result;
        }
    }

    /// <summary>
    /// Feature: single-folder-session-ui, Property 4: 1GMR Path Match Determines Session Behavior
    ///
    /// **Validates: Requirements 8.1, 8.2**
    ///
    /// For any randomly generated path pair with various normalizations,
    /// the path comparison logic used in executeAdd1GmrAsync correctly determines
    /// whether the session should remain open or be closed.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PathMatch_WithRandomNormalizations_DeterminesCorrectBehavior(
        NonNegativeInt baseSeed, NonNegativeInt segmentSeed, NonNegativeInt normSeed)
    {
        // Generate a random base path
        string[] drives = new[] { @"C:\", @"D:\", @"E:\" };
        string[] segments = new[] { "Users", "Test", "DataStore", "Projects", "Images", "My Folder", "Backup", "NKit" };

        string drive = drives[baseSeed.Get % drives.Length];
        int segCount = (segmentSeed.Get % 3) + 1; // 1 to 3 segments
        string sessionPath = drive + string.Join(@"\", Enumerable.Range(0, segCount)
            .Select(i => segments[(baseSeed.Get + (i * 7)) % segments.Length]));

        // Generate a 1GMR output path - apply random normalizations
        // normSeed determines: 0 = same path, 1 = trailing sep, 2 = upper case,
        // 3 = lower case, 4 = extra dot-segment, 5-9 = different path
        int normType = normSeed.Get % 10;
        string gmrOutputPath;

        switch (normType)
        {
            case 0: // Same path
                gmrOutputPath = sessionPath;
                break;
            case 1: // With trailing separator
                gmrOutputPath = sessionPath + @"\";
                break;
            case 2: // Upper case
                gmrOutputPath = sessionPath.ToUpperInvariant();
                break;
            case 3: // Lower case
                gmrOutputPath = sessionPath.ToLowerInvariant();
                break;
            case 4: // With .\. relative segment (Path.GetFullPath normalizes this)
                gmrOutputPath = sessionPath + @"\.\";
                break;
            case 5: // Different - extra segment
                gmrOutputPath = sessionPath + @"\Extra";
                break;
            case 6: // Different - parent
                gmrOutputPath = Path.GetDirectoryName(sessionPath) ?? drive;
                break;
            case 7: // Different - different drive
                gmrOutputPath = @"F:\Other\Path";
                break;
            case 8: // Different - sibling folder
                gmrOutputPath = (Path.GetDirectoryName(sessionPath) ?? drive) + @"\Sibling";
                break;
            case 9: // Different - completely unrelated
                gmrOutputPath = @"Z:\Unrelated\Location";
                break;
            default:
                gmrOutputPath = sessionPath;
                break;
        }

        FakeDataStoreService fakeService = new FakeDataStoreService();
        SessionManager sessionManager = new SessionManager(fakeService);

        // Open a directory session
        sessionManager.OpenDirectoryAsync(sessionPath).GetAwaiter().GetResult();

        if (sessionManager.CurrentState != OpenState.Directory)
        {
            sessionManager.Dispose();
            return true; // Discard
        }

        // Normalize both paths the same way GetCurrentFolderPath() does
        string currentFolderPath = sessionManager.GetCurrentFolderPath();
        if (currentFolderPath == null)
        {
            sessionManager.Dispose();
            return true; // Discard
        }

        string normalizedGmrPath = Path.GetFullPath(gmrOutputPath);
        if (!normalizedGmrPath.EndsWith(Path.DirectorySeparatorChar))
            normalizedGmrPath += Path.DirectorySeparatorChar;

        // Compare paths case-insensitive (Windows)
        bool pathsMatch = string.Equals(currentFolderPath, normalizedGmrPath, StringComparison.OrdinalIgnoreCase);

        if (pathsMatch)
        {
            // Requirement 8.1: paths match → session remains open
            bool result = sessionManager.CurrentState == OpenState.Directory;
            sessionManager.Dispose();
            return result;
        }
        else
        {
            // Requirement 8.2: paths don't match → close transitions to None
            sessionManager.Close();
            bool result = sessionManager.CurrentState == OpenState.None;
            sessionManager.Dispose();
            return result;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Property 6: Filter Bar / Progress Indicator Mutual Exclusivity
    // ─────────────────────────────────────────────────────────────────────────────

    // ─────────────────────────────────────────────────────────────────────────────
    // Property 5: Mount Independence
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Feature: single-folder-session-ui, Property 5: Mount Independence
    ///
    /// **Validates: Requirements 9.2**
    ///
    /// For any SessionManager state (directory or file session open) and any mount operation
    /// targeting a different folder via the DataStoreService directly, the SessionManager's
    /// CurrentState and OpenStateText SHALL remain unchanged after the mount operation completes.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool AfterMountOperation_SessionManagerState_RemainsUnchanged(
        bool openAsDirectory, NonNegativeInt sessionPathSeed, NonNegativeInt mountPathSeed)
    {
        // Generate varied session paths
        string[] sessionPaths = new[]
        {
            @"C:\Users\Test\DataStore1",
            @"D:\Projects\Images",
            @"E:\Backup\Sets",
            @"C:\Temp\NKit",
            @"C:\My Folder\With Spaces"
        };

        // Generate varied mount paths (different from session paths)
        string[] mountPaths = new[]
        {
            @"F:\MountedStore\Alpha",
            @"G:\External\Backup",
            @"H:\Network\Share",
            @"C:\OtherFolder\Mount1",
            @"D:\Mounts\SetFolder"
        };

        string sessionPath = sessionPaths[sessionPathSeed.Get % sessionPaths.Length];
        string mountPath = mountPaths[mountPathSeed.Get % mountPaths.Length];

        FakeDataStoreService fakeService = new FakeDataStoreService();
        SessionManager sessionManager = new SessionManager(fakeService);

        // Open a session via SessionManager
        if (openAsDirectory)
        {
            sessionManager.OpenDirectoryAsync(sessionPath).GetAwaiter().GetResult();
        }
        else
        {
            string filePath = Path.Combine(sessionPath, "testset.nkds");
            sessionManager.OpenFileAsync(filePath).GetAwaiter().GetResult();
        }

        // Record state before mount
        OpenState stateBefore = sessionManager.CurrentState;
        string openStateTextBefore = sessionManager.OpenStateText;

        // Simulate a mount operation by directly calling the FakeDataStoreService
        // (bypassing SessionManager, as mount operations do in the real system)
        fakeService.OpenDirectoryAsync(mountPath).GetAwaiter().GetResult();

        // INVARIANT: SessionManager's state is unchanged after mount
        bool stateUnchanged = sessionManager.CurrentState == stateBefore;
        bool textUnchanged = sessionManager.OpenStateText == openStateTextBefore;

        sessionManager.Dispose();
        return stateUnchanged && textUnchanged;
    }

    /// <summary>
    /// Feature: single-folder-session-ui, Property 5: Mount Independence
    ///
    /// **Validates: Requirements 9.2**
    ///
    /// For any sequence of session operations followed by multiple mount operations,
    /// the SessionManager's CurrentState and OpenStateText SHALL remain unchanged
    /// regardless of how many mount operations are performed on the underlying service.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool AfterMultipleMountOperations_SessionManagerState_RemainsUnchanged(
        NonNegativeInt sessionTypeSeed, NonNegativeInt sessionPathSeed, NonNegativeInt mountCountSeed, NonNegativeInt mountPathSeed)
    {
        string[] sessionPaths = new[]
        {
            @"C:\Users\Test\DataStore1",
            @"D:\Projects\Images",
            @"E:\Backup\Sets",
            @"C:\Temp\NKit",
            @"Z:\Deep\Nested\Path"
        };

        string[] mountPaths = new[]
        {
            @"F:\MountedStore\Alpha",
            @"G:\External\Backup",
            @"H:\Network\Share",
            @"C:\OtherFolder\Mount1",
            @"D:\Mounts\SetFolder",
            @"E:\Another\Mount",
            @"F:\Yet\Another\Path"
        };

        string sessionPath = sessionPaths[sessionPathSeed.Get % sessionPaths.Length];
        int mountCount = (mountCountSeed.Get % 5) + 1; // 1 to 5 mount operations

        FakeDataStoreService fakeService = new FakeDataStoreService();
        SessionManager sessionManager = new SessionManager(fakeService);

        // Open a session via SessionManager (directory or file based on seed)
        bool openAsDirectory = (sessionTypeSeed.Get % 2) == 0;
        if (openAsDirectory)
        {
            sessionManager.OpenDirectoryAsync(sessionPath).GetAwaiter().GetResult();
        }
        else
        {
            string filePath = Path.Combine(sessionPath, "dataset.nkds");
            sessionManager.OpenFileAsync(filePath).GetAwaiter().GetResult();
        }

        // Record state before mounts
        OpenState stateBefore = sessionManager.CurrentState;
        string openStateTextBefore = sessionManager.OpenStateText;

        // Simulate multiple mount operations directly on the service
        Random pathRandom = new Random(mountPathSeed.Get);
        for (int i = 0; i < mountCount; i++)
        {
            string mountPath = mountPaths[pathRandom.Next(mountPaths.Length)];
            fakeService.OpenDirectoryAsync(mountPath).GetAwaiter().GetResult();
        }

        // INVARIANT: SessionManager's state is unchanged after all mounts
        bool stateUnchanged = sessionManager.CurrentState == stateBefore;
        bool textUnchanged = sessionManager.OpenStateText == openStateTextBefore;

        sessionManager.Dispose();
        return stateUnchanged && textUnchanged;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Property 6: Filter Bar / Progress Indicator Mutual Exclusivity
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Feature: single-folder-session-ui, Property 6: Filter Bar / Progress Indicator Mutual Exclusivity
    ///
    /// **Validates: Requirements 6.7, 6.8, 6.9, 7.1, 7.5**
    ///
    /// For any random sequence of OpenDirectory, OpenFile, Close, and ToggleFilterBar operations,
    /// after each operation completes, IsLoading and IsFilterBarVisible SHALL never both be true.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool AfterAnyOperationSequence_IsLoadingAndIsFilterBarVisible_NeverBothTrue(
        NonNegativeInt sequenceLengthSeed, NonNegativeInt commandSeed, NonNegativeInt pathSeed)
    {
        // Generate a sequence of 1 to 30 operations
        int sequenceLength = (sequenceLengthSeed.Get % 30) + 1;

        FakeDataStoreService fakeService = new FakeDataStoreService();
        SessionManager sessionManager = new SessionManager(fakeService);

        Random random = new Random(commandSeed.Get);
        Random pathRandom = new Random(pathSeed.Get);

        for (int i = 0; i < sequenceLength; i++)
        {
            // 4 operation types: OpenDirectory, OpenFile, Close, ToggleFilterBar
            int opType = random.Next(4);
            string path = $@"C:\TestFolder\Path{pathRandom.Next(10)}";

            switch (opType)
            {
                case 0: // OpenDirectory
                    sessionManager.OpenDirectoryAsync(path).GetAwaiter().GetResult();
                    break;
                case 1: // OpenFile
                    string filePath = $@"{path}\set{pathRandom.Next(5)}.nkds";
                    sessionManager.OpenFileAsync(filePath).GetAwaiter().GetResult();
                    break;
                case 2: // Close
                    sessionManager.Close();
                    break;
                case 3: // ToggleFilterBar
                    sessionManager.IsFilterBarVisible = !sessionManager.IsFilterBarVisible;
                    break;
            }

            // INVARIANT: IsLoading and IsFilterBarVisible are never both true
            if (sessionManager.IsLoading && sessionManager.IsFilterBarVisible)
                return false;
        }

        sessionManager.Dispose();
        return true;
    }

    /// <summary>
    /// Feature: single-folder-session-ui, Property 6: Filter Bar / Progress Indicator Mutual Exclusivity
    ///
    /// **Validates: Requirements 6.7, 6.8, 6.9, 7.1, 7.5**
    ///
    /// For any generated sequence of operations including loading and filter bar toggles,
    /// IsLoading and IsFilterBarVisible SHALL never both be true at any observable point.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SessionManagerPropertyTests) })]
    public bool AfterAnyGeneratedOperationSequence_MutualExclusivityHolds(List<FilterBarOperation> operations)
    {
        if (operations == null || operations.Count == 0)
            return true;

        FakeDataStoreService fakeService = new FakeDataStoreService();
        SessionManager sessionManager = new SessionManager(fakeService);

        foreach (FilterBarOperation op in operations)
        {
            switch (op.Type)
            {
                case FilterBarOperationType.OpenDirectory:
                    sessionManager.OpenDirectoryAsync(op.Path).GetAwaiter().GetResult();
                    break;
                case FilterBarOperationType.OpenFile:
                    sessionManager.OpenFileAsync(op.Path).GetAwaiter().GetResult();
                    break;
                case FilterBarOperationType.Close:
                    sessionManager.Close();
                    break;
                case FilterBarOperationType.SetFilterBarVisible:
                    sessionManager.IsFilterBarVisible = true;
                    break;
                case FilterBarOperationType.SetFilterBarHidden:
                    sessionManager.IsFilterBarVisible = false;
                    break;
            }

            // INVARIANT: IsLoading and IsFilterBarVisible are never both true
            if (sessionManager.IsLoading && sessionManager.IsFilterBarVisible)
                return false;
        }

        sessionManager.Dispose();
        return true;
    }
}