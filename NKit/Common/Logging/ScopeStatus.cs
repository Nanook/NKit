namespace Nanook.NKit
{
    /// <summary>
    /// Status of a scope node in the task tree — used by progress tracking and UI rendering.
    /// </summary>
    public enum ScopeStatus
    {
        /// <summary>Not yet started.</summary>
        Pending = 0,

        /// <summary>Currently executing.</summary>
        Running,

        /// <summary>Completed successfully.</summary>
        Done,

        /// <summary>Completed with errors.</summary>
        Error,

        /// <summary>Skipped — not applicable or cancelled.</summary>
        Skipped,
    }
}