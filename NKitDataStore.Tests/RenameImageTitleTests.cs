using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Guards <see cref="DataStore.RenameImageTitle"/> — the set-aware rename that keeps a WiiU
    /// multi-TMD title consistent. Such a title is stored as a <c>TmdAppFolder</c> umbrella
    /// (Name = base) plus one <c>App</c> child per TMD (<c>"{base} [tmd.N]"</c>). The mount recombine
    /// relies on <c>ExtractBaseName(child) == umbrella.Name</c>, so a rename MUST update the umbrella
    /// AND every child together, preserving each child's index. These tests lock that in (the bug was
    /// that both rename callers renamed only one record / dropped the [tmd.N] suffix, breaking mount).
    /// </summary>
    [Collection("ImageBuilder Sequential Tests")]
    public class RenameImageTitleTests : IDisposable
    {
        private readonly string _dir;

        public RenameImageTitleTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), $"RenameTitle_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
        }

        private static void AddStub(DataStore store, string setName, string name, ImageFormat format)
        {
            using IImageWriter writer = store.AddImage(setName, name, "WiiU", format);
            byte[] data = { 1, 2, 3, 4 };
            writer.WriteData(0, data, BlockType.File);
            writer.FinalizeImage(data.Length, 0, 0);
        }

        [Fact]
        public void RenameImageTitle_TmdTitle_RenamesUmbrellaAndAllChildren_PreservingIndex()
        {
            const string setName = "wiiu";
            using DataStore store = new DataStore(_dir);
            store.CreateSet(setName);

            // A WiiU multi-TMD title: umbrella + two tmd children.
            AddStub(store, setName, "Old Game", ImageFormat.TmdAppFolder);
            AddStub(store, setName, "Old Game [tmd.0]", ImageFormat.App);
            AddStub(store, setName, "Old Game [tmd.1]", ImageFormat.App);
            TestDataStoreHelper.WaitForSetIdle(store, setName);

            // Rename via the umbrella record — RenameImageTitle must fan out to the whole group.
            ImageRecord umbrella = store.ListImagesInSet(setName).Single(i => i.Format == ImageFormat.TmdAppFolder);
            int count = store.RenameImageTitle(new GlobalImageKey(setName, umbrella.Id), "New Game");
            TestDataStoreHelper.WaitForSetIdle(store, setName);

            Assert.Equal(3, count); // umbrella + 2 children

            List<ImageRecord> after = store.ListImagesInSet(setName).Where(i => !i.Removed).ToList();

            // Umbrella renamed to the new base (no suffix).
            ImageRecord newUmbrella = after.Single(i => i.Format == ImageFormat.TmdAppFolder);
            Assert.Equal("New Game", newUmbrella.Name);

            // Each child renamed to "New Game [tmd.N]", preserving its original index.
            List<ImageRecord> children = after.Where(i => i.Format == ImageFormat.App).OrderBy(i => i.Name).ToList();
            Assert.Equal(2, children.Count);
            Assert.Equal("New Game [tmd.0]", children[0].Name);
            Assert.Equal("New Game [tmd.1]", children[1].Name);

            // The mount recombine invariant holds: every child's base == the umbrella name.
            foreach (ImageRecord child in children)
                Assert.Equal(newUmbrella.Name, DataStore.ExtractBaseName(child.Name));
        }

        [Fact]
        public void RenameImageTitle_FromChildRecord_StillRenamesWholeGroup()
        {
            const string setName = "wiiu";
            using DataStore store = new DataStore(_dir);
            store.CreateSet(setName);

            AddStub(store, setName, "Old Game", ImageFormat.TmdAppFolder);
            AddStub(store, setName, "Old Game [tmd.0]", ImageFormat.App);
            AddStub(store, setName, "Old Game [tmd.1]", ImageFormat.App);
            TestDataStoreHelper.WaitForSetIdle(store, setName);

            // Identify the group via a CHILD, not the umbrella — base is derived from it.
            ImageRecord child1 = store.ListImagesInSet(setName).Single(i => i.Name == "Old Game [tmd.1]");
            int count = store.RenameImageTitle(new GlobalImageKey(setName, child1.Id), "Fresh Name");
            TestDataStoreHelper.WaitForSetIdle(store, setName);

            Assert.Equal(3, count);
            List<ImageRecord> after = store.ListImagesInSet(setName).Where(i => !i.Removed).ToList();
            Assert.Equal("Fresh Name", after.Single(i => i.Format == ImageFormat.TmdAppFolder).Name);
            Assert.Contains(after, i => i.Name == "Fresh Name [tmd.0]");
            Assert.Contains(after, i => i.Name == "Fresh Name [tmd.1]");
        }

        [Fact]
        public void RenameImageTitle_PlainImage_RenamesOnlyItself()
        {
            const string setName = "wiiu";
            using DataStore store = new DataStore(_dir);
            store.CreateSet(setName);

            AddStub(store, setName, "Plain Title", ImageFormat.Iso);
            AddStub(store, setName, "Other Title", ImageFormat.Iso);
            TestDataStoreHelper.WaitForSetIdle(store, setName);

            ImageRecord plain = store.ListImagesInSet(setName).Single(i => i.Name == "Plain Title");
            int count = store.RenameImageTitle(new GlobalImageKey(setName, plain.Id), "Renamed Title");
            TestDataStoreHelper.WaitForSetIdle(store, setName);

            Assert.Equal(1, count);
            List<ImageRecord> after = store.ListImagesInSet(setName).Where(i => !i.Removed).ToList();
            Assert.Contains(after, i => i.Name == "Renamed Title");
            Assert.Contains(after, i => i.Name == "Other Title"); // unaffected
            Assert.DoesNotContain(after, i => i.Name == "Plain Title");
        }
    }
}
