using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using System.Xml.Linq;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace NKit.Tests.NKDS;

/// <summary>
/// Bug condition exploration test for the toolbar ReflectionBinding AOT fix.
/// 
/// This test verifies that rd.xml contains the nkds-ui assembly directive required
/// for ReflectionBinding to resolve at runtime under NativeAOT trimming.
/// 
/// **Validates: Requirements 1.1, 1.2, 1.3**
/// 
/// EXPECTED: This test FAILS on unfixed code (proving the bug exists).
/// The nkds-ui assembly is missing from rd.xml, so ReflectionBinding cannot
/// resolve Command properties at runtime under NativeAOT trimming.
/// </summary>
public class RdXmlBugConditionTests
{
    // Path to rd.xml relative to the test execution directory
    private static readonly string RdXmlPath = FindRdXmlPath();

    private static string FindRdXmlPath()
    {
        // Walk up from the test bin directory to find the repo root
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "NKDS.UI", "rd.xml");
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("Could not find NKDS.UI/rd.xml from test directory");
    }

    private static string FindXamlFile(string relativePath)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException($"Could not find {relativePath} from test directory");
    }

    /// <summary>
    /// Property 1: Bug Condition - Missing nkds-ui Assembly in rd.xml
    /// 
    /// Parses NKDS.UI/rd.xml and asserts that an Assembly directive with
    /// Name="nkds-ui" and Dynamic="Required All" exists within the Application section.
    /// 
    /// This test encodes the expected behavior. On unfixed code it FAILS,
    /// confirming the bug exists. After the fix, it will PASS.
    /// 
    /// **Validates: Requirements 1.1, 1.2, 1.3**
    /// </summary>
    [Property(MaxTest = 1)]
    public Property NkdsUiAssemblyMustBePreservedInRdXml()
    {
        // Parse rd.xml
        var doc = XDocument.Load(RdXmlPath);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        // Find all Assembly elements in the Application section
        var applicationElement = doc.Root?.Element(ns + "Application");
        var assemblyElements = applicationElement?.Elements(ns + "Assembly") ?? Enumerable.Empty<XElement>();

        // Check if nkds-ui assembly directive exists with Dynamic="Required All"
        var hasNkdsUiAssembly = assemblyElements.Any(el =>
            el.Attribute("Name")?.Value == "nkds-ui" &&
            el.Attribute("Dynamic")?.Value == "Required All");

        return hasNkdsUiAssembly.ToProperty()
            .Label("rd.xml must contain <Assembly Name=\"nkds-ui\" Dynamic=\"Required All\" /> " +
                   "in the <Application> section to preserve reflection metadata for ReflectionBinding");
    }
}
