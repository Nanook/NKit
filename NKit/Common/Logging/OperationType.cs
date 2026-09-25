namespace Nanook.NKit
{
    /// <summary>
    /// The high-level operation being performed. Set once on the operation scope.
    /// </summary>
    public enum OperationType
    {
        None = 0,
        Convert,
        Extract,
        Dedupe,
        Expand,
        Scan,
        Verify,
    }
}