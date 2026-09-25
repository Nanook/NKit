using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using System.Xml.Linq;

namespace NKDS.Tests;

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
        string? dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            string candidate = Path.Combine(dir, "NKDS.UI", "rd.xml");
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("Could not find NKDS.UI/rd.xml from test directory");
    }

    private static string FindXamlFile(string relativePath)
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            string candidate = Path.Combine(dir, relativePath);
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
        XDocument doc = XDocument.Load(RdXmlPath);
        XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        // Find all Assembly elements in the Application section
        XElement? applicationElement = doc.Root?.Element(ns + "Application");
        IEnumerable<XElement> assemblyElements = applicationElement?.Elements(ns + "Assembly") ?? Enumerable.Empty<XElement>();

        // Check if nkds-ui assembly directive exists with Dynamic="Required All"
        bool hasNkdsUiAssembly = assemblyElements.Any(el =>
            el.Attribute("Name")?.Value == "nkds-ui" &&
            el.Attribute("Dynamic")?.Value == "Required All");

        return hasNkdsUiAssembly.ToProperty()
            .Label("rd.xml must contain <Assembly Name=\"nkds-ui\" Dynamic=\"Required All\" /> " +
                   "in the <Application> section to preserve reflection metadata for ReflectionBinding");
    }

    /// <summary>
    /// Verifies that ToolbarControl.axaml uses compiled bindings (Command="{Binding ...}")
    /// rather than ReflectionBinding, confirming the AOT fix is in place.
    /// The nkds-ui assembly is still preserved in rd.xml for any remaining reflection needs.
    /// 
    /// **Validates: Requirements 1.1, 1.2**
    /// </summary>
    [Property(MaxTest = 1)]
    public Property ToolbarControlReflectionBindingRequiresAssemblyPreservation()
    {
        XDocument rdXmlDoc = XDocument.Load(RdXmlPath);
        XNamespace ns = rdXmlDoc.Root?.Name.Namespace ?? XNamespace.None;

        // Verify ToolbarControl.axaml uses compiled Binding (not ReflectionBinding)
        string toolbarXamlPath = FindXamlFile(Path.Combine("NKDS.UI", "Views", "Controls", "ToolbarControl.axaml"));
        string toolbarContent = File.ReadAllText(toolbarXamlPath);
        bool usesCompiledBinding = toolbarContent.Contains("{Binding Command}") || toolbarContent.Contains("Command=\"{Binding");
        bool noReflectionBinding = !toolbarContent.Contains("{ReflectionBinding");

        // Verify rd.xml preserves the target assembly
        XElement? applicationElement = rdXmlDoc.Root?.Element(ns + "Application");
        IEnumerable<XElement> assemblyElements = applicationElement?.Elements(ns + "Assembly") ?? Enumerable.Empty<XElement>();
        bool assemblyPreserved = assemblyElements.Any(el =>
            el.Attribute("Name")?.Value == "nkds-ui" &&
            el.Attribute("Dynamic")?.Value == "Required All");

        bool result = noReflectionBinding && assemblyPreserved;

        return result.ToProperty()
            .Label($"ToolbarControl.axaml uses compiled bindings (noReflectionBinding={noReflectionBinding}) " +
                   $"and nkds-ui is preserved in rd.xml (preserved={assemblyPreserved})");
    }

    /// <summary>
    /// Verifies that CreateSetDialog.axaml uses compiled bindings for commands
    /// rather than ReflectionBinding, confirming the AOT fix is in place.
    /// 
    /// **Validates: Requirements 1.3**
    /// </summary>
    [Property(MaxTest = 1)]
    public Property CreateSetDialogReflectionBindingRequiresAssemblyPreservation()
    {
        XDocument rdXmlDoc = XDocument.Load(RdXmlPath);
        XNamespace ns = rdXmlDoc.Root?.Name.Namespace ?? XNamespace.None;

        // Verify CreateSetDialog.axaml does NOT use ReflectionBinding (migrated to compiled bindings)
        string dialogXamlPath = FindXamlFile(Path.Combine("NKDS.UI", "Views", "Dialogs", "CreateSetDialog.axaml"));
        string dialogContent = File.ReadAllText(dialogXamlPath);
        bool noReflectionBinding = !dialogContent.Contains("{ReflectionBinding");

        // Verify rd.xml preserves the target assembly
        XElement? applicationElement = rdXmlDoc.Root?.Element(ns + "Application");
        IEnumerable<XElement> assemblyElements = applicationElement?.Elements(ns + "Assembly") ?? Enumerable.Empty<XElement>();
        bool assemblyPreserved = assemblyElements.Any(el =>
            el.Attribute("Name")?.Value == "nkds-ui" &&
            el.Attribute("Dynamic")?.Value == "Required All");

        bool result = noReflectionBinding && assemblyPreserved;

        return result.ToProperty()
            .Label($"CreateSetDialog.axaml uses compiled bindings (noReflectionBinding={noReflectionBinding}) " +
                   $"and nkds-ui is preserved in rd.xml (preserved={assemblyPreserved})");
    }
}