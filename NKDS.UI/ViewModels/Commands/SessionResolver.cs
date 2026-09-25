using NkdsUi.Models;
using NKitDataStore;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Shared utility for resolving DataStore paths from session models.
/// Replaces duplicated private helper methods across command handlers.
/// </summary>
public static class SessionResolver
{
    /// <summary>
    /// The virtual set name representing all sets combined.
    /// </summary>
    public const string AllSetsName = "All";

    /// <summary>
    /// Resolves the DataStore directory path and original path from a session.
    /// If the session path ends with the DataStore file extension, returns the parent directory.
    /// </summary>
    public static (string dataStorePath, string originalPath) ResolveDataStorePath(ImageSessionModel session)
    {
        if (session.Path.EndsWith(DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            string dataStorePath = Path.GetDirectoryName(session.Path) ?? session.Path;
            return (dataStorePath, session.Path);
        }

        return (session.Path, session.Path);
    }

    /// <summary>
    /// Finds the first session that owns the given set name.
    /// Search order: images collection match → DataStore.ListSetNames() match → first session fallback.
    /// Returns null for null or empty session lists.
    /// </summary>
    public static ImageSessionModel? FindSessionForSet(
        IReadOnlyList<ImageSessionModel>? sessions, string setName)
    {
        if (sessions == null || sessions.Count == 0)
            return null;

        // Phase 1: Search images collections (ordinal, case-sensitive)
        foreach (ImageSessionModel session in sessions)
        {
            if (session.Images.Any(img => img.SetName == setName))
                return session;
        }

        // Phase 2: Search DataStore.ListSetNames(), suppressing exceptions
        foreach (ImageSessionModel session in sessions)
        {
            try
            {
                if (session.DataStore.ListSetNames().Contains(setName))
                    return session;
            }
            catch
            {
                // Suppress and continue searching
            }
        }

        // Phase 3: Fall back to first session
        return sessions[0];
    }

    /// <summary>
    /// Combines session lookup and path resolution: finds the session owning the set name,
    /// then resolves its DataStore directory path.
    /// Returns null for null or empty session lists.
    /// </summary>
    public static string? ResolveDataStorePathForSet(
        IReadOnlyList<ImageSessionModel>? sessions, string setName)
    {
        if (sessions == null || sessions.Count == 0)
            return null;

        ImageSessionModel? session = FindSessionForSet(sessions, setName);
        if (session == null)
            return null;

        return ResolveDataStorePath(session).dataStorePath;
    }
}