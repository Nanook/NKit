using System.Collections.Generic;

namespace Nanook.NKit
{
    public enum AddressMode { Area, Disc, Relative }

    /// <summary>
    /// Hold information about the area
    /// </summary>
    public class AreaInfo
    {
        internal AreaInfo(long imageOffset, AreaType type, int areaNumber)
        {
            this.ImageOffset = imageOffset;
            this.Type = type;
            this.AreaNo = areaNumber;
            this.Properties = null;
            SetBlock(0x8000, 0, 0x8000, 0x8000 * 0x40); //32k block size, 2MiB section size
        }

        internal void Setup(long imageOffset, AreaType type)
        {
            this.ImageOffset = imageOffset;
            this.Type = type;
        }

        public long ImageOffset { get; private set; }

        public AreaType Type { get; private set; }

        public AddressMode FsAddressMode { get; internal set; }

        public long FsOffset { get; internal set; } //Multisession iso can have missing data (lead out?) Offset filesystem offsets by this
        public long BaseOffset { get; internal set; } //DC track/area can span from 3 to 5 (5 had filesystem from 3), ps3 spans areas, ps2 rb2

        public int BlockSize { get; private set; }
        public int BlockFsOffset { get; private set; }
        public int BlockFsSize { get; private set; }

        public int SectionSize { get; private set; }

        internal Properties Properties { get; set; }

        public bool IsEncrypted { get; private set; }
        public bool IsEncryptionSupported { get; private set; }
        public bool HasSecurity { get; private set; }

        /// <summary>
        /// Increments for each differnet type of data / headers / content etc
        /// </summary>
        public int AreaNo { get; private set; }

        internal AreaInfo Clone() => this.Clone(this.ImageOffset, this.Type, this.AreaNo);

        internal AreaInfo Clone(long imageOffset, AreaType type, int areaNo)
        {
            AreaInfo ai = new AreaInfo(imageOffset, type, areaNo);
            ai.SetBlock(this.BlockSize, this.BlockFsOffset, this.BlockFsSize, this.SectionSize);
            ai.SetSecurity(this.IsEncrypted, this.IsEncryptionSupported, this.HasSecurity);
            ai.Properties = this.Properties == null ? null : this.Properties.Clone();

            if (ai.AreaNo == this.AreaNo)
            {
                ai.BaseOffset = this.BaseOffset; //only really used by Dreamcast WipePartition 5
                ai.FsAddressMode = this.FsAddressMode;
            }
            return ai;
        }

        internal void SetProperties(params string[] names) => this.Properties = new Properties(names);

        internal void SetSecurity(bool isEncrypted, bool isEncryptedSupported, bool hasSecurity)
        {
            this.IsEncrypted = isEncrypted;
            this.IsEncryptionSupported = isEncryptedSupported;
            this.HasSecurity = hasSecurity;
        }

        internal void SetBlock(int blockSize, int blockFsOffset, int blockFsSize, int sectionSize)
        {
            this.BlockSize = blockSize;
            this.BlockFsOffset = blockFsOffset;
            this.BlockFsSize = blockFsSize;
            this.SectionSize = sectionSize;
        }

        //unit testable because the logic got horrible at one point
        internal static AreaInfo NextArea(long currImagePos, long fsImageOffset, long fsSize, long imageSize, long defaultNextOffset, AreaType defaultNextType, List<IImageArea> areas, int areaNo)
        {
            AreaType next = (AreaType)int.MaxValue;

            //set next pos to default
            if (areas != null)
            {
                int i;
                for (i = 0; i < areas.Count; i++)
                {
                    if (areas[i].ImageOffset > currImagePos)
                        break;
                }
                if (i < areas.Count && (defaultNextOffset == -1 || areas[i].ImageOffset <= defaultNextOffset))
                {
                    defaultNextOffset = areas[i].ImageOffset;
                    next = areas[i].AreaType;
                }
            }

            if (next == (AreaType)int.MaxValue)
            {
                if (currImagePos < defaultNextOffset)
                    next = defaultNextType;
                else
                {
                    defaultNextOffset = imageSize;
                    next = AreaType.None;
                }
            }

            return new AreaInfo(defaultNextOffset, next, areaNo);
        }
    }
}