namespace NkdsUi.Models;

/// <summary>
/// Static definitions for all registerable OS integration entries.
/// </summary>
public static class AssociationEntries
{
    public static readonly AssociationEntry DirectoryOpen = new()
    {
        Id = "dir-open",
        Label = "NKDS Open",
        Category = AssociationCategory.DirectoryContextMenu,
        CommandArgTemplate = "--datastore \"{0}\""
    };

    public static readonly AssociationEntry DirectoryMount = new()
    {
        Id = "dir-mount",
        Label = "NKDS Mount",
        Category = AssociationCategory.DirectoryContextMenu,
        CommandArgTemplate = "--action mount --datastore \"{0}\""
    };

    public static readonly AssociationEntry DirectoryAddDir = new()
    {
        Id = "dir-adddir",
        Label = "NKDS Add Dir",
        Category = AssociationCategory.DirectoryContextMenu,
        CommandArgTemplate = "--action adddir --input \"{0}\""
    };

    public static readonly AssociationEntry DirectoryAddDirNew = new()
    {
        Id = "dir-adddir-new",
        Label = "NKDS Add Dir New",
        Category = AssociationCategory.DirectoryContextMenu,
        CommandArgTemplate = "--action adddirnew --input \"{0}\""
    };

    public static readonly AssociationEntry FileOpenSet = new()
    {
        Id = "file-open-set",
        Label = "NKDS Open Set",
        Category = AssociationCategory.FileContextMenu,
        CommandArgTemplate = "--set \"{0}\""
    };

    public static readonly AssociationEntry FileMountSet = new()
    {
        Id = "file-mount-set",
        Label = "NKDS Mount Set",
        Category = AssociationCategory.FileContextMenu,
        CommandArgTemplate = "--action mount --set \"{0}\""
    };

    public static readonly AssociationEntry FileAdd = new()
    {
        Id = "file-add",
        Label = "NKDS Add",
        Category = AssociationCategory.FileContextMenu,
        CommandArgTemplate = "--action add --input \"{0}\""
    };

    public static readonly AssociationEntry FileAddNew = new()
    {
        Id = "file-add-new",
        Label = "NKDS Add New",
        Category = AssociationCategory.FileContextMenu,
        CommandArgTemplate = "--action addnew --input \"{0}\""
    };

    public static readonly AssociationEntry DoubleClickOpen = new()
    {
        Id = "dblclick-open",
        Label = "Open as Set",
        Category = AssociationCategory.DoubleClickHandler,
        CommandArgTemplate = "--set \"{0}\""
    };
}