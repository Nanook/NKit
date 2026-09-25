using System;

namespace Nanook.NKit.App.Cli
{
    /// <summary>A user-facing CLI parse/validation error. Carries the verb (if known) so the host
    /// can print the relevant help.</summary>
    public sealed class CliException : Exception
    {
        public CliException(string message, string verb = null) : base(message)
        {
            Verb = verb;
        }

        /// <summary>The verb context for help rendering, or null for general help.</summary>
        public string Verb { get; }
    }
}