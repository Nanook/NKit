using System;
using System.IO;
using System.Threading;
using NKit.Tests;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using System.Xml.Linq;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace NKit.Tests.NKDS;

/// <summary>
/// Preservation property tests for the toolbar ReflectionBinding AOT fix.
/// 
/// These tests verify that existing rd.xml assembly directives, compiled bindings,
/// and project build behavior remain unchanged after the fix is applied.
/// 
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
/// 
/// EXPECTED: These tests PASS on unfixed code (confirming baseline behavior to preserve).
/// </summary>
public class RdXmlPreservationTests
{
    /// <summary>
    /// The 15 third-party assembly directives that exist in rd.xml on unfixed code.
    /// These must remain present and unchanged after any fix is applied.
    /// </summary>
    private static readonly string[] ExpectedAssemblies = new[]
    {
        "Avalonia.Controls.DataGrid",
        "Material.Avalonia",
        "Material.Avalonia.DataGrid",
        "Material.Icons.Avalonia",
        "Svg.Controls.Skia.Avalonia",
        "ReactiveUI.Avalonia",
        "ReactiveUI",
        "ExCSS",
        "Material.Styles",
        "Microsoft.CSharp",
        "System.Linq.Expressions",
        "System.Private.Xml",
        "Avalonia.FreeDesktop",
        "Tmds.DBus.Protocol",
        "Avalonia.Dialogs"
    };

    private static readonly string RdXmlPath = FindRdXmlPath();

    private static string FindRdXmlPath()
    {
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

    private static string FindProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "NKDS.UI", "NKDS.UI.csproj");
            if (File.Exists(candidate))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("Could not find project root from test directory");
    }

    /// <summary>
    /// Property 2a: All existing assembly directives in rd.xml must remain present
    /// and unchanged after the fix (no removals, no modifications).
    /// 
    /// For all assemblies in the expected set, the rd.xml must contain an Assembly
    /// element with the exact Name and Dynamic="Required All" attribute.
    /// 
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
    /// </summary>
    [Property(MaxTest = 1)]
    public Property AllExistingAssemblyDirectivesMustRemainPresent()
    {
        var doc = XDocument.Load(RdXmlPath);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        var applicationElement = doc.Root?.Element(ns + "Application");
        var assemblyElements = applicationElement?.Elements(ns + "Assembly") ?? Enumerable.Empty<XElement>();

        var presentAssemblies = assemblyElements
            .Where(el => el.Attribute("Dynamic")?.Value == "Required All")
            .Select(el => el.Attribute("Name")?.Value)
            .Where(name => name != null)
            .ToHashSet();

        var missingAssemblies = ExpectedAssemblies
            .Where(expected => !presentAssemblies.Contains(expected))
            .ToList();

        var allPresent = missingAssemblies.Count == 0;

        return allPresent.ToProperty()
            .Label(allPresent
                ? "All 15 existing assembly directives are present in rd.xml with Dynamic=\"Required All\""
                : $"Missing assemblies in rd.xml: {string.Join(", ", missingAssemblies)}");
    }

    /// <summary>
    /// Property 2b: The rd.xml structure must remain valid XML with a
    /// Directives > Application root structure.
    /// 
    /// This ensures the fix does not corrupt the XML structure that the
    /// .NET linker depends on.
    /// 
    /// **Validates: Requirements 3.1, 3.3**
    /// </summary>
    [Property(MaxTest = 1)]
    public Property RdXmlMustHaveValidDirectivesApplicationStructure()
    {
        var doc = XDocument.Load(RdXmlPath);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        // Root element must be "Directives"
        var rootIsDirectives = doc.Root?.Name.LocalName == "Directives";

        // Must contain an "Application" child element
        var applicationElement = doc.Root?.Element(ns + "Application");
        var hasApplication = applicationElement != null;

        // Application must contain at least one Assembly element
        var assemblyCount = applicationElement?.Elements(ns + "Assembly").Count() ?? 0;
        var hasAssemblies = assemblyCount > 0;

        var isValid = rootIsDirectives && hasApplication && hasAssemblies;

        return isValid.ToProperty()
            .Label($"rd.xml structure: root=Directives ({rootIsDirectives}), " +
                   $"has Application ({hasApplication}), " +
                   $"has Assembly elements ({hasAssemblies}, count={assemblyCount})");
    }

    /// <summary>
    /// Property 2c: Each existing assembly directive must have exactly
    /// Dynamic="Required All" — no attribute modifications allowed.
    /// 
    /// This uses property-based testing to verify that for any randomly selected
    /// assembly from the expected set, its directive is correctly preserved.
    /// 
    /// **Validates: Requirements 3.1, 3.3**
    /// </summary>
    [Property(MaxTest = 15)]
    public Property RandomlySelectedExistingAssemblyMustBePreserved()
    {
        var gen = Gen.Elements(ExpectedAssemblies);

        return Prop.ForAll(gen.ToArbitrary(), assemblyName =>
        {
            var doc = XDocument.Load(RdXmlPath);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            var applicationElement = doc.Root?.Element(ns + "Application");
            var assemblyElements = applicationElement?.Elements(ns + "Assembly") ?? Enumerable.Empty<XElement>();

            var matchingElement = assemblyElements.FirstOrDefault(el =>
                el.Attribute("Name")?.Value == assemblyName);

            var exists = matchingElement != null;
            var hasDynamicRequiredAll = matchingElement?.Attribute("Dynamic")?.Value == "Required All";

            return (exists && hasDynamicRequiredAll)
                .Label($"Assembly '{assemblyName}' must exist with Dynamic=\"Required All\" " +
                       $"(exists={exists}, dynamic={matchingElement?.Attribute("Dynamic")?.Value ?? "null"})");
        });
    }

    /// <summary>
    /// Property 2d: The NKDS.UI project must have compiled bindings enabled by default
    /// and reference rd.xml as a TrimmerRootDescriptor.
    /// 
    /// This verifies the project configuration that enables compiled bindings
    /// remains intact — if this changes, compiled bindings would stop working.
    /// 
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [Property(MaxTest = 1)]
    public Property ProjectMustHaveCompiledBindingsAndTrimmerRootDescriptor()
    {
        var projectRoot = FindProjectRoot();
        var csprojPath = Path.Combine(projectRoot, "NKDS.UI", "NKDS.UI.csproj");
        var csprojContent = File.ReadAllText(csprojPath);

        var hasCompiledBindings = csprojContent.Contains(
            "<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>");
        var hasTrimmerRootDescriptor = csprojContent.Contains(
            "<TrimmerRootDescriptor Include=\"$(MSBuildThisFileDirectory)rd.xml\" />");

        var isValid = hasCompiledBindings && hasTrimmerRootDescriptor;

        return isValid.ToProperty()
            .Label($"NKDS.UI.csproj: CompiledBindings={hasCompiledBindings}, " +
                   $"TrimmerRootDescriptor={hasTrimmerRootDescriptor}");
    }

    /// <summary>
    /// Property 2e: ToolbarControl.axaml must use compiled bindings for display properties.
    /// 
    /// Verifies that the compiled bindings for IsEnabled, Label, IconName, Tooltip,
    /// ShowDividerBefore, and IsChecked are present in the XAML — these must continue
    /// to work after the fix.
    /// 
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(MaxTest = 1)]
    public Property ToolbarControlMustUseCompiledBindingsForDisplayProperties()
    {
        var projectRoot = FindProjectRoot();
        var xamlPath = Path.Combine(projectRoot, "NKDS.UI", "Views", "Controls", "ToolbarControl.axaml");
        var content = File.ReadAllText(xamlPath);

        // These compiled bindings must be present (they use {Binding ...} with x:DataType)
        var hasIsEnabled = content.Contains("{Binding IsEnabled}") || content.Contains("IsEnabled=\"{Binding IsEnabled}\"");
        var hasLabel = content.Contains("{Binding Label}");
        var hasIconName = content.Contains("{Binding IconName");
        var hasTooltip = content.Contains("{Binding Tooltip}");
        var hasShowDividerBefore = content.Contains("{Binding ShowDividerBefore}");
        var hasIsChecked = content.Contains("{Binding IsChecked}");

        var allPresent = hasIsEnabled && hasLabel && hasIconName && hasTooltip && hasShowDividerBefore && hasIsChecked;

        return allPresent.ToProperty()
            .Label($"ToolbarControl compiled bindings: IsEnabled={hasIsEnabled}, Label={hasLabel}, " +
                   $"IconName={hasIconName}, Tooltip={hasTooltip}, ShowDividerBefore={hasShowDividerBefore}, " +
                   $"IsChecked={hasIsChecked}");
    }

    /// <summary>
    /// Property 2f: CreateSetDialog.axaml must use compiled bindings for form properties.
    /// 
    /// Verifies that the compiled bindings for SetName, SetNameError, ShardSizeOptions,
    /// SelectedShardSize, BlockSizeOptions, and SelectedBlockSize are present.
    /// 
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(MaxTest = 1)]
    public Property CreateSetDialogMustUseCompiledBindingsForFormProperties()
    {
        var projectRoot = FindProjectRoot();
        var xamlPath = Path.Combine(projectRoot, "NKDS.UI", "Views", "Dialogs", "CreateSetDialog.axaml");
        var content = File.ReadAllText(xamlPath);

        var hasSetName = content.Contains("{Binding SetName");
        var hasSetNameError = content.Contains("{Binding SetNameError}");
        var hasShardSizeOptions = content.Contains("{Binding ShardSizeOptions}");
        var hasSelectedShardSize = content.Contains("{Binding SelectedShardSize");
        var hasBlockSizeOptions = content.Contains("{Binding BlockSizeOptions}");
        var hasSelectedBlockSize = content.Contains("{Binding SelectedBlockSize");

        var allPresent = hasSetName && hasSetNameError && hasShardSizeOptions &&
                         hasSelectedShardSize && hasBlockSizeOptions && hasSelectedBlockSize;

        return allPresent.ToProperty()
            .Label($"CreateSetDialog compiled bindings: SetName={hasSetName}, SetNameError={hasSetNameError}, " +
                   $"ShardSizeOptions={hasShardSizeOptions}, SelectedShardSize={hasSelectedShardSize}, " +
                   $"BlockSizeOptions={hasBlockSizeOptions}, SelectedBlockSize={hasSelectedBlockSize}");
    }

    /// <summary>
    /// Build verification: The NKDS.UI project file must be valid XML and contain
    /// the essential build configuration for compiled bindings and AOT trimming.
    /// 
    /// This confirms that the project structure supports compiled bindings resolution.
    /// A successful `dotnet build` depends on these settings being present and correct.
    /// If any of these are missing or malformed, compiled bindings would fail.
    /// 
    /// **Validates: Requirements 3.1, 3.2, 3.3**
    /// </summary>
    [Fact]
    public void ProjectFileMustBeValidAndContainBuildConfiguration()
    {
        var projectRoot = FindProjectRoot();
        var csprojPath = Path.Combine(projectRoot, "NKDS.UI", "NKDS.UI.csproj");

        // Project file must be valid XML
        var doc = XDocument.Load(csprojPath);
        Assert.NotNull(doc.Root);

        var csprojContent = File.ReadAllText(csprojPath);

        // Must have compiled bindings enabled
        Assert.Contains("<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>", csprojContent);

        // Must reference rd.xml as TrimmerRootDescriptor
        Assert.Contains("<TrimmerRootDescriptor Include=\"$(MSBuildThisFileDirectory)rd.xml\" />", csprojContent);

        // Must have PublishAot enabled
        Assert.Contains("<PublishAot>true</PublishAot>", csprojContent);

        // Must have TrimMode=link
        Assert.Contains("<TrimMode>link</TrimMode>", csprojContent);

        // Must have the correct assembly name
        Assert.Contains("<AssemblyName>nkds-ui</AssemblyName>", csprojContent);

        // rd.xml must exist alongside the project file
        var rdXmlPath = Path.Combine(projectRoot, "NKDS.UI", "rd.xml");
        Assert.True(File.Exists(rdXmlPath), "rd.xml must exist in the NKDS.UI project directory");

        // rd.xml must be included as Content for output
        Assert.Contains("<Content Include=\"rd.xml\">", csprojContent);
    }
}