namespace NkdsUi.Models;

public enum VerifyResultStatus
{
    /// <summary>No verification was performed.</summary>
    None,

    /// <summary>Verification succeeded.</summary>
    VerifySuccess,

    /// <summary>Image was not verified (no DAT match, verification disabled).</summary>
    Unverified,

    /// <summary>Verification failed (CRC mismatch, data corruption).</summary>
    VerifyFailed
}