using Nanook.NKit;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    /// <summary>
    /// Tests the read-only <see cref="IAreaFileSystemView"/> projections over an immutable file
    /// snapshot: Primary (whole list), System (IsSystemFile), and safe index-based enumeration.
    /// The view is what parallel section processors read instead of the live mutable collection.
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class AreaFileSystemViewTests
    {
        // Minimal IFsFile stub — only the members the view reads (identity + IsSystemFile).
        private sealed class StubFsFile : IFsFile
        {
            public StubFsFile(string name, long fsOffset, bool system)
            {
                Name = name;
                FullName = "/" + name;
                FsOffset = fsOffset;
                IsSystemFile = system;
            }
            public string Name { get; }
            public IFsFolder Parent => null;
            public string Path => "/";
            public bool IsMissing => false;
            public bool IsLastFile { get; set; }
            public int SplitIndex => 0;
            public IFsFileParts SplitParts => null;
            public string FullName { get; }
            public long FsSize => 0x800;
            public ulong XxHash { get; set; }
            public uint Crc { get; set; }
            public uint GapCrc { get; set; }
            public bool IsSystemFile { get; }
            public long FsOffset { get; }
            public long PostGapSize => 0;
            public long PostGapFsOffset => FsOffset + FsSize;
            public IFsFile Clone() => this;
        }

        private static AreaFileSystemView build(params IFsFile[] files)
            => new AreaFileSystemView(0, 0x100000, PartitionType.Game, false, files, null, FileSystemKind.Unknown);

        [Fact]
        public void Primary_ExposesWholeListByIndex()
        {
            IFsFile a = new StubFsFile("a", 0x0, false);
            IFsFile b = new StubFsFile("b", 0x1000, false);
            IFsFile c = new StubFsFile("c", 0x2000, true);
            AreaFileSystemView view = build(a, b, c);

            Assert.Equal(3, view.Primary.FileCount);
            Assert.Same(a, view.Primary.File(0));
            Assert.Same(b, view.Primary.File(1));
            Assert.Same(c, view.Primary.File(2));
        }

        [Fact]
        public void System_ProjectsOnlyIsSystemFileEntries()
        {
            IFsFile a = new StubFsFile("boot.bin", 0x0, true);
            IFsFile b = new StubFsFile("game.dat", 0x1000, false);
            IFsFile c = new StubFsFile("fst.bin", 0x2000, true);
            AreaFileSystemView view = build(a, b, c);

            Assert.Equal(2, view.System.FileCount);
            Assert.Same(a, view.System.File(0));
            Assert.Same(c, view.System.File(1));
            // Primary still holds all three.
            Assert.Equal(3, view.Primary.FileCount);
        }

        [Fact]
        public void System_EmptyWhenNoSystemFiles()
        {
            AreaFileSystemView view = build(
                new StubFsFile("a", 0x0, false),
                new StubFsFile("b", 0x1000, false));
            Assert.Equal(0, view.System.FileCount);
        }

        [Fact]
        public void Files_EnumerableIteratesByIndex()
        {
            IFsFile a = new StubFsFile("a", 0x0, false);
            IFsFile b = new StubFsFile("b", 0x1000, true);
            AreaFileSystemView view = build(a, b);

            List<IFsFile> viaEnum = view.Primary.Files.ToList();
            Assert.Equal(2, viaEnum.Count);
            Assert.Same(a, viaEnum[0]);
            Assert.Same(b, viaEnum[1]);
        }

        [Fact]
        public void FileSystems_SingleViewWhenNoPerFsTyping()
        {
            // StubFsFile is not an FstFile, so there is no FsType typing — a single view over all.
            AreaFileSystemView view = build(
                new StubFsFile("a", 0x0, false),
                new StubFsFile("b", 0x1000, false));
            Assert.Single(view.FileSystems);
            Assert.Equal(2, view.FileSystems[0].FileCount);
        }

        [Fact]
        public void FileSystems_GroupsRealFstFilesByFsType()
        {
            // Real FstFile entries carry FsType via their Links (parent folder's FsType), so the
            // view splits them into per-file-system views (ISO9660 / Joliet).
            Nanook.NKit.Iso.Iso9660.FstFolder isoFolder = new Nanook.NKit.Iso.Iso9660.FstFolder(FsType.Iso9660);
            Nanook.NKit.Iso.Iso9660.FstFolder jolietFolder = new Nanook.NKit.Iso.Iso9660.FstFolder(FsType.Joliet);
            IFsFile isoA = new Nanook.NKit.Iso.Iso9660.FstFile(isoFolder, "a", FsType.Iso9660, 0x0, 0x800, Nanook.NKit.Iso.Iso9660.FsItemType.File);
            IFsFile isoB = new Nanook.NKit.Iso.Iso9660.FstFile(isoFolder, "b", FsType.Iso9660, 0x1000, 0x800, Nanook.NKit.Iso.Iso9660.FsItemType.File);
            IFsFile jol = new Nanook.NKit.Iso.Iso9660.FstFile(jolietFolder, "c", FsType.Joliet, 0x2000, 0x800, Nanook.NKit.Iso.Iso9660.FsItemType.File);

            AreaFileSystemView view = build(isoA, isoB, jol);

            // Two logical file systems, and Primary still has all three.
            Assert.Equal(2, view.FileSystems.Count);
            Assert.Equal(3, view.Primary.FileCount);
            int total = view.FileSystems.Sum(fs => fs.FileCount);
            Assert.Equal(3, total);
        }

        [Fact]
        public void Snapshot_IsImmutable_UnaffectedBySourceListMutation()
        {
            List<IFsFile> src = new List<IFsFile>
            {
                new StubFsFile("a", 0x0, false),
                new StubFsFile("b", 0x1000, false),
            };
            // Build from a COPY (as TryBuild does via ToArray) — mutating src must not change the view.
            AreaFileSystemView view = build(src.ToArray());
            src.Add(new StubFsFile("c", 0x2000, false));

            Assert.Equal(2, view.Primary.FileCount); // still the snapshot count
        }
    }
}