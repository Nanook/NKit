namespace NkdsUi.Models;

/// <summary>
/// Represents the current session mode in the single-folder UI model.
/// </summary>
public enum OpenState
{
    /// <summary>No session is open.</summary>
    None,

    /// <summary>A directory (folder) session is open — all sets loaded.</summary>
    Directory,

    /// <summary>A single .nkds file session is open.</summary>
    File
}