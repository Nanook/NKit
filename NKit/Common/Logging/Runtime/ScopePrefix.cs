using System.Collections.Generic;

namespace Nanook.NKit.Runtime
{
    /// <summary>
    /// Resolves a scope to its short bracketed source tag (e.g. <c>[Wii]</c>, <c>[Convert]</c>) for
    /// sink rendering. The tag is derived from the scope — never hand-typed into message text — so
    /// it stays consistent and cannot drift per call site (Req 9 / design P4).
    /// <para>
    /// Resolution order: a well-known <c>"prefix"</c> property on the scope (or any ancestor),
    /// falling back to the scope's own <see cref="ILogScope.Name"/>. Producers set the prefix once
    /// on a scope (via its properties); sinks call <see cref="Resolve"/>.
    /// </para>
    /// </summary>
    internal static class ScopePrefix
    {
        /// <summary>Well-known scope property key carrying the source tag.</summary>
        public const string PrefixKey = "prefix";

        /// <summary>
        /// The innermost meaningful prefix for a scope: walks up from the scope looking for a
        /// <c>prefix</c> property, else uses the nearest non-empty scope name. Returns null if
        /// nothing is found.
        /// </summary>
        public static string Resolve(ILogScope scope)
        {
            for (ILogScope s = scope; s != null; s = s.Parent)
            {
                IReadOnlyDictionary<string, object> props = s.Properties;
                object val;
                if (props != null && props.TryGetValue(PrefixKey, out val) && val != null)
                {
                    string tag = val.ToString();
                    if (!string.IsNullOrEmpty(tag))
                        return tag;
                }
            }
            for (ILogScope s = scope; s != null; s = s.Parent)
            {
                if (!string.IsNullOrEmpty(s.Name))
                    return s.Name;
            }
            return null;
        }

        /// <summary>The full scope chain root→leaf as names, for file/structured rendering.</summary>
        public static string Chain(ILogScope scope, string separator = " > ")
        {
            List<string> names = new List<string>();
            for (ILogScope s = scope; s != null; s = s.Parent)
            {
                if (!string.IsNullOrEmpty(s.Name))
                    names.Add(s.Name);
            }
            names.Reverse();
            return string.Join(separator, names.ToArray());
        }
    }
}