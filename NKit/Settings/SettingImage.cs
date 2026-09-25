namespace Nanook.NKit
{
    internal class SettingDisc
    {
        internal SettingDisc(string id8)
        {
            Id8 = id8;
        }

        public string Id8 { get; }
    }

    internal class JunkIdSubstitution : SettingDisc
    {
        internal JunkIdSubstitution(string id8, string junkId) : base(id8)
        {
            JunkId = junkId;
        }

        public string JunkId { get; }
    }


    internal class DataPatches : SettingDisc
    {
        internal DataPatches(string id8, long offset, byte[] data) : base(id8)
        {
            Data = data;
            Offset = offset;
        }

        public byte[] Data { get; }
        public long Offset { get; }
    }
}