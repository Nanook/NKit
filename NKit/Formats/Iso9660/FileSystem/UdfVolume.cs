using System.Collections.Generic;
using System.Linq;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class UdfVolume
    {
        public ImageHeaderPvd Pvd { get; private set; }
        public List<UdfDescriptorTag> Vrs { get; private set; }
        public UdfAnchorVolumeDescriptorPointer VolPtr { get; private set; }
        public UdfLogicalVolumeDescriptor Volume { get; }
        public UdfPartitionDescriptor Partition { get; }
        public UdfPartitionMap[] Maps { get; }
        public long PartitionFsOffset { get; private set; }
        public long FileDescriptorFsOffset { get; internal set; }
        public long Size => 0x800; // read all then calculate record type
        public bool IsMain { get; private set; }
        public string TypeName { get; private set; }
        public int Priority { get; private set; }
        public long FsOffset { get => this.PartitionOffsets[1]; internal set => this.PartitionOffsets[1] = value; }

        public long[] PartitionOffsets { get; }

        public UdfVolume(ImageHeaderPvd pvd, List<UdfDescriptorTag> volumeRecognitionSequence, UdfAnchorVolumeDescriptorPointer volumePointer, long baseOffset)
        {
            this.Pvd = pvd;
            this.Vrs = volumeRecognitionSequence;
            this.VolPtr = volumePointer;
            this.Partition = this.Vrs.FirstOrDefault(a => a.TagId == UdfTagId.PartitionDescriptor) as UdfPartitionDescriptor;

            this.IsMain = Vrs[0].TagLocation == VolPtr.MainDescriptorSequence.Location;
            this.Volume = (UdfLogicalVolumeDescriptor)Vrs.FirstOrDefault(a => a.TagId == UdfTagId.LogicalVolumeDescriptor);
            if (this.Volume == null)
                return;

            //List<UdfPartitionDescriptor> ptn = Vrs.Where(a => a.TagId == UdfTagId.PartitionDescriptor).Cast<UdfPartitionDescriptor>().OrderBy(a => a.PartitionNumber).ToList();
            this.PartitionFsOffset = (this.Partition.PartitionStartingLocation * (long)this.Volume.LogicalBlockSize) - baseOffset;
            this.Maps = Volume.PartitionMaps.ToArray();
            this.PartitionOffsets = new[] { this.PartitionFsOffset, 0 };
        }
    }
}