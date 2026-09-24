namespace NkdsUi.Models;

public enum ToolbarOperationKind
{
    OpenDataStore,
    OpenFile,
    CloseSet,
    CreateSet,
    Add,
    AddDir,
    Add1Gmr,
    Verify,
    Export,
    Remove,
    Restore,
    Compact,
    Rollback,
    Stats,
    Graphs,
    Mount,
    Group,
    Yaml,
    Dat
}

public sealed class ToolbarOperation
{
    public ToolbarOperationKind Kind { get; init; }
    public string Label { get; init; } = "";
    public string IconName { get; init; } = "";
    public string Tooltip { get; init; } = "";
    public int Group { get; init; }
}

public static class ToolbarOperations
{
    public static IReadOnlyList<ToolbarOperation> All { get; } =
    [
        new() { Kind = ToolbarOperationKind.OpenDataStore, Label = "Open", IconName = "Open", Tooltip = "Open - Open all Sets in a DataStore directory", Group = 1 },
        new() { Kind = ToolbarOperationKind.OpenFile, Label = "Open Set", IconName = "OpenFile", Tooltip = "Open Set - Open a specific .nkds file", Group = 1 },
        new() { Kind = ToolbarOperationKind.CloseSet, Label = "Close", IconName = "CloseSet", Tooltip = "Close - Close open DataStore or set", Group = 1 },
        new() { Kind = ToolbarOperationKind.CreateSet, Label = "Create Set", IconName = "Create", Tooltip = "Create Set - Create a new set in the DataStore", Group = 1 },
        new() { Kind = ToolbarOperationKind.Add, Label = "Add", IconName = "Add", Tooltip = "Add Images - Import images into the active set", Group = 2 },
        new() { Kind = ToolbarOperationKind.AddDir, Label = "Add Dir", IconName = "AddDir", Tooltip = "Add Directory - Archive a regular directory tree into the active set", Group = 2 },
        new() { Kind = ToolbarOperationKind.Add1Gmr, Label = "Add 1GMR", IconName = "Add1Gmr", Tooltip = "Add 1GMR - Import related images in to sets - 1 Game, Many Roms", Group = 2 },
        new() { Kind = ToolbarOperationKind.Verify, Label = "Verify", IconName = "Verify", Tooltip = "Verify - Check integrity of selected images", Group = 2 },
        new() { Kind = ToolbarOperationKind.Export, Label = "Export", IconName = "Export", Tooltip = "Export - Export selected images to files", Group = 2 },
        new() { Kind = ToolbarOperationKind.Remove, Label = "Remove", IconName = "Remove", Tooltip = "Remove - Soft-delete selected images from the set", Group = 3 },
        new() { Kind = ToolbarOperationKind.Restore, Label = "Restore", IconName = "Restore", Tooltip = "Restore - Restore previously removed images", Group = 3 },
        new() { Kind = ToolbarOperationKind.Compact, Label = "Compact", IconName = "Compact", Tooltip = "Compact - Reclaim disk space by purging removed images. Including index defragmentation", Group = 3 },
        new() { Kind = ToolbarOperationKind.Rollback, Label = "Rollback", IconName = "Rollback", Tooltip = "Rollback - Undo recent additions to the active set", Group = 3 },
        new() { Kind = ToolbarOperationKind.Stats, Label = "Stats", IconName = "Stats", Tooltip = "Stats - Calculate storage statistics and ratios", Group = 4 },
        new() { Kind = ToolbarOperationKind.Graphs, Label = "Graph", IconName = "Graphs", Tooltip = "Graph - Show storage statistics graphs", Group = 4 },
        new() { Kind = ToolbarOperationKind.Mount, Label = "Mount", IconName = "Mount", Tooltip = "Mount - Mount the listed images as a virtual filesystem", Group = 5 },
        new() { Kind = ToolbarOperationKind.Group, Label = "Grouping", IconName = "Group", Tooltip = "Grouping - Open threshold grouping window", Group = 6 },
        new() { Kind = ToolbarOperationKind.Dat, Label = "Dat", IconName = "Dat", Tooltip = "Dat - Verify images against a dat file", Group = 4 },
    ];
}