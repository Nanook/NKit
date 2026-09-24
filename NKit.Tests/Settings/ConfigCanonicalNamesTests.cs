using Nanook.NKit;
using System.Collections.Generic;
using Xunit;

namespace NKit.Tests.Settings
{
    /// <summary>
    /// Verifies the config alias fallback: a config written with the new canonical keys resolves,
    /// and the legacy keys still resolve too. Uses the override-dictionary AppSettings path so no
    /// file is needed.
    /// </summary>
    [Trait("Area", "Settings")]
    public class ConfigCanonicalNamesTests
    {
        // Build AppSettings with overrides (config disabled) and read back a per-system value.
        private static AppSettings New(Dictionary<string, string> overrides)
        {
            overrides["cfg"] = "n"; // no external config
            return new AppSettings((string)null, overrides);
        }

        [Fact]
        public void CanonicalRootKeys_Resolve()
        {
            AppSettings s = New(new Dictionary<string, string>
            {
                { "task", "convert" },
                { "recursive", "y" },   // canonical (legacy: r)
                { "archives", "n" },    // canonical (legacy: arc)
                { "verify", "y" },      // canonical (legacy: v)
            });

            Assert.Equal(TaskType.Convert, s.TaskType);
            Assert.True(s[SystemType.Wii].R);          // recursive → R
            Assert.False(s[SystemType.Wii].Arc);       // archives:n → Arc false
        }

        [Fact]
        public void CanonicalPerSystemKeys_Resolve_ViaAlias()
        {
            AppSettings s = New(new Dictionary<string, string>
            {
                { "task", "convert" },
                { "wii:format", "rvz:zstd:19:128k:16" },   // canonical (legacy: convert)
            });
            SystemSettings wii = s[SystemType.Wii];
            wii.Initialise(SystemType.Wii, TaskType.Convert, s.GetLog((m, l) => { }), s.DatManager);
            Assert.Equal("rvz:zstd:19:128k:16", wii.Convert);
        }

        [Fact]
        public void LegacyKeys_StillResolve()
        {
            AppSettings s = New(new Dictionary<string, string>
            {
                { "task", "convert" },
                { "r", "y" },                               // legacy still works
                { "wii:convert", "wbfs:y" },                // legacy still works
            });
            Assert.True(s[SystemType.Wii].R);
            SystemSettings wii = s[SystemType.Wii];
            wii.Initialise(SystemType.Wii, TaskType.Convert, s.GetLog((m, l) => { }), s.DatManager);
            Assert.Equal("wbfs:y", wii.Convert);
        }
    }
}