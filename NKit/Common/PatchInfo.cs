namespace Nanook.NKit
{
    internal class PatchInfo : IPatchInfo
    {
        public bool MarkForPatching { get; set; }
        public bool MarkForCalculatedData { get; set; }
        public uint PrePatchCrc { get; set; }
        public ulong PrePatchXxHash { get; set; }
        public uint PrePatchCrcDecrypted { get; set; }
        public bool ScrubbingChanged { get; set; }

        public IPatchInfo Clone()
        {
            return new PatchInfo()
            {
                ScrubbingChanged = this.ScrubbingChanged,
                MarkForPatching = this.MarkForPatching,
                MarkForCalculatedData = this.MarkForCalculatedData,
                PrePatchCrc = this.PrePatchCrc,
                PrePatchCrcDecrypted = this.PrePatchCrcDecrypted,
                PrePatchXxHash = this.PrePatchXxHash
            };
        }
    }
}