using Nanook.NKit.Nintendo.WiiGc;
using Nanook.NKit.Nintendo.WiiU;

namespace Nanook.NKit.Nintendo
{
    internal class PartitionInfo
    {

        internal PartitionInfo(PartitionType type, long offset, int table, long tablePos)
        {
            ImageOffset = offset;
            Table = table;
            Type = type;
            TableOffset = tablePos;
        }

        public PartitionType Type { get; private set; }
        public SignedStatus SignedStatus { get; internal set; }
        public long ImageOffset { get; internal set; }
        internal int Table { get; set; }
        internal long TableOffset { get; set; }

        internal string Id { get; set; }

        public bool IsMainContent { get; internal set; } //true if contains game data or is otherwise considered the payload (falst if not required)

        internal FixPartition FixPartition { get; set; }
        internal bool IsPlaceholder { get; set; }


        internal SiData WiiUSiData { get; set; }
        public ulong WiiUTitleId { get; internal set; }

    }

}