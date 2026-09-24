namespace Nanook.NKit
{
    internal interface IPatchInfo
    {
        bool MarkForPatching { get; set; }
        bool MarkForCalculatedData { get; set; }
        uint PrePatchCrc { get; set; }
        ulong PrePatchXxHash { get; set; }
        uint PrePatchCrcDecrypted { get; set; }
        bool ScrubbingChanged { get; set; }

        IPatchInfo Clone();
    }
}