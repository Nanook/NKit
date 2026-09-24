using FsCheck;
using FsCheck.Xunit;
using NkdsUi.Models;
using NkdsUi.ViewModels.Commands;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for SessionResolver.FindSessionForSet.
///
/// Feature: session-resolver-consolidation, Property 3: Session lookup returns first matching session in list order
/// </summary>
public class SessionResolverPropertyTests : IDisposable
{
    private readonly string _baseTestDirectory;
    private readonly List<DataStore> _disposableStores = new();

    public SessionResolverPropertyTests()
    {
        _baseTestDirectory = Path.Combine(Path.GetTempPath(), $"NKitSessionResolverTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_baseTestDirectory);
    }

    public void Dispose()
    {
        foreach (DataStore store in _disposableStores)
        {
            try { store.Dispose(); } catch { }
        }

        if (Directory.Exists(_baseTestDirectory))
        {
            try
            {
                Thread.Sleep(50);
                Directory.Delete(_baseTestDirectory, recursive: true);
            }
            catch { }
        }
    }

    /// <summary>
    /// Creates a DataStore in a unique subdirectory with the specified set names.
    /// </summary>
    private DataStore createDataStoreWithSets(params string[] setNames)
    {
        string dir = Path.Combine(_baseTestDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        DataStore store = new DataStore(dir);
        _disposableStores.Add(store);

        foreach (string setName in setNames)
        {
            store.CreateSet(setName);
        }

        return store;
    }

    /// <summary>
    /// Creates a DataStore in a unique subdirectory with no sets.
    /// </summary>
    private DataStore createEmptyDataStore()
    {
        string dir = Path.Combine(_baseTestDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        DataStore store = new DataStore(dir);
        _disposableStores.Add(store);
        return store;
    }

    /// <summary>
    /// Creates an ImageSessionModel with the given images and DataStore.
    /// </summary>
    private static ImageSessionModel createSession(string path, DataStore dataStore, params ImageRecord[] images)
    {
        return new ImageSessionModel(
            Guid.NewGuid().ToString(),
            path,
            null,
            dataStore,
            images.ToList());
    }

    /// <summary>
    /// **Validates: Requirements 2.1, 2.3, 2.4, 2.6, 2.7, 6.4**
    ///
    /// Property 3: Session lookup returns first matching session in list order.
    ///
    /// When multiple sessions have images matching the set name, the first session
    /// in list order is returned (phase 1: image-based search).
    /// </summary>
    [Property(MaxTest = 100)]
    public bool FindSessionForSet_ReturnsFirstImageMatch_InListOrder(PositiveInt sessionCountWrapper)
    {
        int sessionCount = Math.Min(sessionCountWrapper.Get, 10); // Cap to keep test fast
        string targetSetName = $"TargetSet_{Guid.NewGuid():N}";

        // Create sessions where ALL have images matching the target set name
        List<ImageSessionModel> sessions = new List<ImageSessionModel>();
        for (int i = 0; i < sessionCount; i++)
        {
            DataStore store = createEmptyDataStore();
            ImageRecord image = new ImageRecord { Id = i + 1, Name = $"Image{i}", SetName = targetSetName };
            sessions.Add(createSession($@"C:\Path{i}", store, image));
        }

        ImageSessionModel result = SessionResolver.FindSessionForSet(sessions, targetSetName);

        // Must return the FIRST session in list order
        return result == sessions[0];
    }

    /// <summary>
    /// **Validates: Requirements 2.1, 2.3, 2.4, 2.6, 2.7, 6.4**
    ///
    /// Property 3: Session lookup returns first matching session in list order.
    ///
    /// When no session has images matching but multiple sessions' DataStore.ListSetNames()
    /// contain the set name, the first such session in list order is returned (phase 2).
    /// </summary>
    [Property(MaxTest = 100)]
    public bool FindSessionForSet_ReturnsFirstDataStoreMatch_InListOrder(PositiveInt matchIndexWrapper)
    {
        int totalSessions = 5;
        // The match index determines which session first contains the target set in its DataStore
        int matchIndex = matchIndexWrapper.Get % totalSessions;
        string targetSetName = $"DSSet_{Guid.NewGuid():N}";

        List<ImageSessionModel> sessions = new List<ImageSessionModel>();
        for (int i = 0; i < totalSessions; i++)
        {
            DataStore store;
            if (i >= matchIndex)
            {
                // Sessions at matchIndex and beyond have the target set in their DataStore
                store = createDataStoreWithSets(targetSetName);
            }
            else
            {
                // Sessions before matchIndex do NOT have the target set
                store = createEmptyDataStore();
            }

            // No images match the target set name (force phase 2)
            ImageRecord image = new ImageRecord { Id = i + 1, Name = $"Image{i}", SetName = "OtherSet" };
            sessions.Add(createSession($@"C:\Path{i}", store, image));
        }

        ImageSessionModel result = SessionResolver.FindSessionForSet(sessions, targetSetName);

        // Must return the session at matchIndex (first DataStore match in list order)
        return result == sessions[matchIndex];
    }

    /// <summary>
    /// **Validates: Requirements 2.1, 2.3, 2.4, 2.6, 2.7, 6.4**
    ///
    /// Property 3: Session lookup returns first matching session in list order.
    ///
    /// When no session matches by images or DataStore listing, the first session
    /// in the list is returned as fallback (phase 3).
    /// </summary>
    [Property(MaxTest = 100)]
    public bool FindSessionForSet_FallsBackToFirstSession_WhenNoMatch(PositiveInt sessionCountWrapper)
    {
        int sessionCount = Math.Min(sessionCountWrapper.Get, 10); // Cap to keep test fast
        string targetSetName = $"NonExistent_{Guid.NewGuid():N}";

        List<ImageSessionModel> sessions = new List<ImageSessionModel>();
        for (int i = 0; i < sessionCount; i++)
        {
            // No images match, and DataStore has no sets matching targetSetName
            DataStore store = createDataStoreWithSets($"UnrelatedSet{i}");
            ImageRecord image = new ImageRecord { Id = i + 1, Name = $"Image{i}", SetName = "OtherSet" };
            sessions.Add(createSession($@"C:\Path{i}", store, image));
        }

        ImageSessionModel result = SessionResolver.FindSessionForSet(sessions, targetSetName);

        // Must fall back to the first session
        return result == sessions[0];
    }

    /// <summary>
    /// **Validates: Requirements 2.1, 2.3, 2.4, 2.6, 2.7, 6.4**
    ///
    /// Property 3: Session lookup returns first matching session in list order.
    ///
    /// Image-based matching (phase 1) takes priority over DataStore-based matching (phase 2).
    /// Even if an earlier session has the set in its DataStore, a later session with
    /// a matching image in phase 1 wins because phase 1 is evaluated first across ALL sessions.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool FindSessionForSet_ImageMatchTakesPriorityOverDataStoreMatch(PositiveInt seed)
    {
        string targetSetName = $"PrioritySet_{Guid.NewGuid():N}";

        // Session 0: has target set in DataStore but NOT in images
        DataStore store0 = createDataStoreWithSets(targetSetName);
        ImageSessionModel session0 = createSession(@"C:\Path0", store0,
            new ImageRecord { Id = 1, Name = "Image0", SetName = "OtherSet" });

        // Session 1: has target set in images (phase 1 match)
        DataStore store1 = createEmptyDataStore();
        ImageSessionModel session1 = createSession(@"C:\Path1", store1,
            new ImageRecord { Id = 2, Name = "Image1", SetName = targetSetName });

        List<ImageSessionModel> sessions = new List<ImageSessionModel> { session0, session1 };

        ImageSessionModel result = SessionResolver.FindSessionForSet(sessions, targetSetName);

        // Phase 1 (images) is evaluated first across all sessions.
        // Session 1 has the image match, so it should be returned.
        return result == session1;
    }

    /// <summary>
    /// **Validates: Requirements 2.1, 2.3, 2.4, 2.6, 2.7, 6.4**
    ///
    /// Property 3: Session lookup returns first matching session in list order.
    ///
    /// Image matching is case-sensitive (ordinal comparison).
    /// A session whose image SetName differs only in case should NOT match in phase 1.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool FindSessionForSet_ImageMatch_IsCaseSensitive(NonEmptyString setNameWrapper)
    {
        string setName = setNameWrapper.Get;

        // Skip if the set name is already all-uppercase (can't test case sensitivity)
        if (setName == setName.ToUpperInvariant())
            return true; // vacuously true

        string targetSetName = setName;
        string wrongCaseSetName = setName.ToUpperInvariant();

        // Session 0: has image with WRONG case - should NOT match phase 1
        DataStore store0 = createEmptyDataStore();
        ImageSessionModel session0 = createSession(@"C:\Path0", store0,
            new ImageRecord { Id = 1, Name = "Image0", SetName = wrongCaseSetName });

        // Session 1: has image with CORRECT case - should match phase 1
        DataStore store1 = createEmptyDataStore();
        ImageSessionModel session1 = createSession(@"C:\Path1", store1,
            new ImageRecord { Id = 2, Name = "Image1", SetName = targetSetName });

        List<ImageSessionModel> sessions = new List<ImageSessionModel> { session0, session1 };

        ImageSessionModel result = SessionResolver.FindSessionForSet(sessions, targetSetName);

        // Session 1 should be returned because session 0's image has wrong case
        return result == session1;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Property 4: Exception suppression during session lookup
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// **Validates: Requirements 2.5, 6.5**
    ///
    /// Property 4: Exception suppression during session lookup.
    /// For any session list where ALL sessions' DataStore.ListSetNames() throws an exception
    /// (null DataStore → NullReferenceException), SessionResolver.FindSessionForSet SHALL
    /// suppress those exceptions and return the first session as fallback.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool ExceptionSuppression_AllSessionsThrow_ReturnsFallback(
        NonNegativeInt countSeed, NonNegativeInt pathSeed)
    {
        // Generate 1-10 sessions, ALL with null DataStore (throws NullReferenceException on ListSetNames)
        int sessionCount = (countSeed.Get % 10) + 1;

        string[] paths = new[]
        {
            @"C:\Users\Test\DataStore",
            @"D:\Projects\Images",
            @"E:\Backup\Sets",
            @"C:\Temp\NKit",
            @"C:\My Folder\With Spaces"
        };

        List<ImageSessionModel> sessions = new List<ImageSessionModel>();
        for (int i = 0; i < sessionCount; i++)
        {
            string path = paths[(pathSeed.Get + i) % paths.Length];
            sessions.Add(new ImageSessionModel(
                Guid.NewGuid().ToString(),
                path,
                null,
                null!, // null DataStore → ListSetNames() throws NullReferenceException
                new List<ImageRecord>())); // empty images → no Phase 1 match
        }

        // Use a set name that won't match any images (images are empty)
        string setName = "NonExistentSet";

        // Act: should NOT throw, should return first session as fallback
        ImageSessionModel result = null;
        bool noException = true;
        try
        {
            result = SessionResolver.FindSessionForSet(sessions, setName);
        }
        catch
        {
            noException = false;
        }

        // Assert: no exception propagated AND result is the first session (fallback)
        return noException && result == sessions[0];
    }

    /// <summary>
    /// **Validates: Requirements 2.5, 6.5**
    ///
    /// Property 4: Exception suppression during session lookup.
    /// For a session list where some sessions throw and a later session has a matching
    /// set name in its images, the method SHALL suppress exceptions in Phase 2 and still
    /// find the match via the images-based search (Phase 1 runs before Phase 2).
    /// </summary>
    [Property(MaxTest = 200)]
    public bool ExceptionSuppression_ThrowingSessionsBeforeMatch_StillFindsImageMatch(
        NonNegativeInt throwCountSeed, NonNegativeInt pathSeed)
    {
        // Generate 1-5 throwing sessions followed by one session with a matching image
        int throwCount = (throwCountSeed.Get % 5) + 1;
        string targetSetName = "TargetSet";

        string[] paths = new[]
        {
            @"C:\Users\Test\DataStore",
            @"D:\Projects\Images",
            @"E:\Backup\Sets",
            @"C:\Temp\NKit",
            @"C:\My Folder\With Spaces"
        };

        List<ImageSessionModel> sessions = new List<ImageSessionModel>();

        // Add sessions with null DataStore (would throw if Phase 2 is reached)
        for (int i = 0; i < throwCount; i++)
        {
            string path = paths[(pathSeed.Get + i) % paths.Length];
            sessions.Add(new ImageSessionModel(
                Guid.NewGuid().ToString(),
                path,
                null,
                null!, // throws on ListSetNames
                new List<ImageRecord>())); // no matching images
        }

        // Add a session that has the target set name in its images
        ImageSessionModel matchingSession = new ImageSessionModel(
            Guid.NewGuid().ToString(),
            @"C:\MatchingSession",
            null,
            null!, // DataStore doesn't matter since Phase 1 finds the match
            new List<ImageRecord>
            {
                new ImageRecord { SetName = targetSetName, Name = "image1" }
            });
        sessions.Add(matchingSession);

        // Act
        ImageSessionModel result = null;
        bool noException = true;
        try
        {
            result = SessionResolver.FindSessionForSet(sessions, targetSetName);
        }
        catch
        {
            noException = false;
        }

        // Assert: no exception, and the matching session is returned (Phase 1 match)
        return noException && result == matchingSession;
    }

    /// <summary>
    /// **Validates: Requirements 2.5, 6.5**
    ///
    /// Property 4: Exception suppression during session lookup.
    /// For a mixed session list where some sessions throw from ListSetNames and others
    /// don't, the method SHALL suppress all exceptions and return a valid result.
    /// Sessions with non-matching images but throwing DataStores still allow fallback.
    /// </summary>
    [Property(MaxTest = 200)]
    public bool ExceptionSuppression_MixedThrowingPattern_AlwaysReturnsValidResult(
        NonNegativeInt countSeed, NonNegativeInt pathSeed)
    {
        // Generate 2-8 sessions with a mix of null DataStores (all will throw in Phase 2)
        int sessionCount = (countSeed.Get % 7) + 2;
        string setName = "SearchSet";

        string[] paths = new[]
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

        List<ImageSessionModel> sessions = new List<ImageSessionModel>();
        for (int i = 0; i < sessionCount; i++)
        {
            string path = paths[(pathSeed.Get + i) % paths.Length];
            // All sessions have null DataStore (will throw) and non-matching images
            sessions.Add(new ImageSessionModel(
                Guid.NewGuid().ToString(),
                path,
                null,
                null!,
                new List<ImageRecord>
                {
                    // Add images with different set names so Phase 1 doesn't match
                    new ImageRecord { SetName = $"OtherSet{i}", Name = $"image{i}" }
                }));
        }

        // Act: should NOT throw
        ImageSessionModel result = null;
        bool noException = true;
        try
        {
            result = SessionResolver.FindSessionForSet(sessions, setName);
        }
        catch
        {
            noException = false;
        }

        // Assert: no exception propagated AND result is the first session (fallback)
        return noException && result == sessions[0];
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Property 5: Combined method equivalence
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// **Validates: Requirements 3.2, 3.3, 5.5, 6.1**
    ///
    /// Property 5: Combined method equivalence.
    /// For any non-empty session list and any set name,
    /// ResolveDataStorePathForSet(sessions, setName) equals
    /// ResolveDataStorePath(FindSessionForSet(sessions, setName)).dataStorePath.
    ///
    /// This verifies that the combined convenience method produces string-identical
    /// results to calling the two underlying methods separately.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool CombinedMethod_EquivalentTo_FindThenResolve(
        PositiveInt sessionCountWrapper, NonNegativeInt contentSeed, NonEmptyString setNameWrapper)
    {
        string setName = setNameWrapper.Get;
        Random rng = new Random(contentSeed.Get);
        int sessionCount = Math.Min(sessionCountWrapper.Get, 5); // Cap to keep test fast

        // Generate sessions with varying image collections and paths.
        // Use null DataStores — Phase 2 (ListSetNames) will throw NullReferenceException
        // which is suppressed, exercising the exception suppression + fallback path.
        // Phase 1 (images) is exercised by randomly including matching images.
        List<ImageSessionModel> sessions = new List<ImageSessionModel>();
        for (int i = 0; i < sessionCount; i++)
        {
            List<ImageRecord> images = generateImagesForEquivalence(rng, setName);
            string path = generateSessionPathForEquivalence(rng);

            sessions.Add(new ImageSessionModel(
                Guid.NewGuid().ToString(),
                path,
                null,
                null!, // null DataStore — ListSetNames throws, which is suppressed
                images));
        }

        // Act: call the combined method
        string combinedResult = SessionResolver.ResolveDataStorePathForSet(sessions, setName);

        // Act: call the two-step equivalent
        ImageSessionModel foundSession = SessionResolver.FindSessionForSet(sessions, setName);
        string twoStepResult = foundSession != null
            ? SessionResolver.ResolveDataStorePath(foundSession).dataStorePath
            : null;

        // Assert: both approaches produce the same result
        return combinedResult == twoStepResult;
    }

    /// <summary>
    /// **Validates: Requirements 3.2, 3.3, 5.5, 6.1**
    ///
    /// Property 5: Combined method equivalence (with real DataStores).
    /// Same equivalence property but using real DataStores with filesystem-safe set names,
    /// exercising the Phase 2 (DataStore.ListSetNames) lookup path.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool CombinedMethod_EquivalentTo_FindThenResolve_WithRealDataStores(
        PositiveInt sessionCountWrapper, NonNegativeInt contentSeed, NonNegativeInt setNameSeed)
    {
        // Use filesystem-safe set names to avoid IO errors
        string[] safeSetNames = new[] { "GameSet", "WiiImages", "BackupSet", "TestData", "Archive" };
        string setName = safeSetNames[setNameSeed.Get % safeSetNames.Length];
        Random rng = new Random(contentSeed.Get);
        int sessionCount = Math.Min(sessionCountWrapper.Get, 4); // Cap to keep test fast

        List<ImageSessionModel> sessions = new List<ImageSessionModel>();
        for (int i = 0; i < sessionCount; i++)
        {
            List<ImageRecord> images = generateImagesForEquivalence(rng, setName);
            string path = generateSessionPathForEquivalence(rng);

            // Create a real DataStore (may or may not include the target set)
            DataStore store;
            if (rng.Next(3) == 0)
            {
                store = createDataStoreWithSets(setName, $"OtherDS{rng.Next(50)}");
            }
            else
            {
                store = createDataStoreWithSets($"OtherDS{rng.Next(50)}");
            }

            sessions.Add(createSession(path, store, images.ToArray()));
        }

        // Act: call the combined method
        string combinedResult = SessionResolver.ResolveDataStorePathForSet(sessions, setName);

        // Act: call the two-step equivalent
        ImageSessionModel foundSession = SessionResolver.FindSessionForSet(sessions, setName);
        string twoStepResult = foundSession != null
            ? SessionResolver.ResolveDataStorePath(foundSession).dataStorePath
            : null;

        // Assert: both approaches produce the same result
        return combinedResult == twoStepResult;
    }

    /// <summary>
    /// Generates a list of images for the equivalence test. With some probability,
    /// includes an image with the target set name to exercise the Phase 1 (images) match path.
    /// </summary>
    private static List<ImageRecord> generateImagesForEquivalence(Random rng, string targetSetName)
    {
        List<ImageRecord> images = new List<ImageRecord>();
        int imageCount = rng.Next(0, 4); // 0-3 images

        for (int i = 0; i < imageCount; i++)
        {
            // 40% chance of matching the target set name
            string imageSetName = rng.Next(10) < 4
                ? targetSetName
                : $"OtherSet{rng.Next(100)}";

            images.Add(new ImageRecord
            {
                Id = rng.Next(1, 10000),
                Name = $"Image{rng.Next(1000)}",
                SetName = imageSetName,
                Size = rng.Next(1000, 1000000)
            });
        }

        return images;
    }

    /// <summary>
    /// Generates a session path that is either a .nkds file path or a plain directory path.
    /// </summary>
    private static string generateSessionPathForEquivalence(Random rng)
    {
        char drive = (char)('C' + rng.Next(0, 5));
        string dir = $"Store{rng.Next(100)}";

        // 50% chance of .nkds file path vs plain directory
        if (rng.Next(2) == 0)
        {
            string setFile = $"set{rng.Next(50)}{DataStore.DatabaseFileExtension}";
            return $@"{drive}:\Data\{dir}\{setFile}";
        }
        else
        {
            return $@"{drive}:\Data\{dir}";
        }
    }
}