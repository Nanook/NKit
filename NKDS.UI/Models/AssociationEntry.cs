namespace NkdsUi.Models;

/// <summary>
/// Defines a single registerable OS integration item (e.g., a context menu entry or double-click handler).
/// </summary>
public sealed class AssociationEntry
{
    /// <summary>Unique identifier for this entry (e.g., "dir-open", "file-open-set", "dblclick-open").</summary>
    public required string Id { get; init; }

    /// <summary>Display label shown in the Settings Panel (e.g., "NKDS Open").</summary>
    public required string Label { get; init; }

    /// <summary>The category this entry belongs to.</summary>
    public required AssociationCategory Category { get; init; }

    /// <summary>The command-line argument template used when launching the app.</summary>
    public required string CommandArgTemplate { get; init; }
}

/// <summary>
/// Categorizes an association entry by its OS integration type.
/// </summary>
public enum AssociationCategory
{
    /// <summary>Right-click context menu entry for directories.</summary>
    DirectoryContextMenu,

    /// <summary>Right-click context menu entry for .nkds files.</summary>
    FileContextMenu,

    /// <summary>Double-click default handler for .nkds files.</summary>
    DoubleClickHandler
}