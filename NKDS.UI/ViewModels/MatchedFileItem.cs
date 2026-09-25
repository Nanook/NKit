namespace NkdsUi.ViewModels;

/// <summary>
/// Represents a file added to the Add 1GMR dialog with its match result.
/// Used for the preview DataGrid showing filename-to-set routing.
/// </summary>
public class MatchedFileItem : ViewModelBase
{
    /// <summary>
    /// The display filename (without path).
    /// </summary>
    public string FileName { get; init; } = "";

    /// <summary>
    /// The full file path, used during import execution.
    /// </summary>
    public string FilePath { get; init; } = "";

    /// <summary>
    /// The sanitized game set name from 1GMR routing, or "No Match" if unmatched.
    /// </summary>
    public string MatchedSetName { get; init; } = "";

    /// <summary>
    /// Whether this file matched a game entry. False = dimmed in UI.
    /// </summary>
    public bool IsMatched { get; init; }
}