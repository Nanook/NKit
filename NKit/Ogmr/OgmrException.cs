using System;

namespace Nanook.NKit.Ogmr
{
    /// <summary>
    /// Exception thrown when 1GMR YAML parsing or validation fails.
    /// </summary>
    public sealed class OgmrException : Exception
    {
        public OgmrException(string message)
            : base(message)
        {
        }

        public OgmrException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}