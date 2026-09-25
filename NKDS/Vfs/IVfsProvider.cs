namespace Nanook.NKit.Vfs
{
    // Minimal abstraction over VfsModel so platform adapters can use the same core logic
    internal interface IVfsProvider
    {
        string DataStorePath { get; }
        IEnumerable<IFsItem> GetScanFsItems(string path, string mask, char separator);
        IFsItem GetScanFsItem(string path, char separator);
        VfsContext GetScanFsItemAsContext(string path);
    }
}