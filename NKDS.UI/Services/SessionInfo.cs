using NkdsUi.Models;

namespace NkdsUi.Services;

/// <summary>
/// Tracks the details of the currently open session within SessionManager.
/// </summary>
internal record SessionInfo(
    string SessionId,
    string FolderPath,
    string? SetName,
    OpenState Mode,
    string OriginalOpenPath
);