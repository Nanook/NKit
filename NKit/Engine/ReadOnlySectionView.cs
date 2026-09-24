using System.Collections.Generic;
using System.IO;

namespace Nanook.NKit
{
    /// <summary>
    /// Internal adapter that presents an <see cref="ISection"/> as the restricted
    /// <see cref="IReadOnlySection"/> handed to external embedding consumers. Wrapping (rather than
    /// handing out the section directly) is what CLOSES the leak whereby a consumer could downcast a
    /// public <c>ISection</c> back to the internal <c>ISectionProcessor</c> and drive the pipeline:
    /// this wrapper only forwards the safe read-only members and copy-out reads.
    /// </summary>
    internal sealed class ReadOnlySectionView : IReadOnlySection
    {
        private readonly ISection _section;

        public ReadOnlySectionView(ISection section)
        {
            _section = section;
        }

        public long ImageOffset => _section.ImageOffset;
        public long Size => _section.Size;
        public long AreaOffset => _section.AreaOffset;
        public long FsOffset => _section.FsOffset;
        public long FsSize => _section.FsSize;
        public AreaType Type => _section.Type;
        public uint Crc => _section.Crc;
        public uint CrcDecrypted => _section.CrcDecrypted;
        public ulong XxHash => _section.XxHash;
        public bool IsEncrypted => _section.IsEncrypted;
        public AreaInfo AreaInfo => _section.AreaInfo;
        public IAreaFileSystemView AreaFileSystem => _section.AreaFileSystem;

        public IEnumerable<IReadOnlySectionItem> Items
        {
            get
            {
                SectionItems items = _section.Items;
                if (items == null)
                    yield break;
                foreach (ISectionItem item in items)
                    yield return new ReadOnlySectionItemView(item);
            }
        }

        public byte[] ReadBytes(int fsOffset, int size) => _section.ReadBytes(fsOffset, size);

        public void Read(int fsOffset, int size, Stream toStream) => _section.Read(fsOffset, size, toStream);
    }

    /// <summary>Internal adapter presenting an <see cref="ISectionItem"/> as <see cref="IReadOnlySectionItem"/>.</summary>
    internal sealed class ReadOnlySectionItemView : IReadOnlySectionItem
    {
        private readonly ISectionItem _item;

        public ReadOnlySectionItemView(ISectionItem item)
        {
            _item = item;
        }

        public IFsFile FsFile => _item.FsFile;
        public long ImageOffset => _item.ImageOffset;
        public long FileOffsetInSection => _item.File?.FsOffset ?? 0;
        public long Size => _item.File?.FsSize ?? 0;
        public IReadOnlyList<string> FileSystems => _item.FileSystems;
    }
}
