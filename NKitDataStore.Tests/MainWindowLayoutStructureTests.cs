using System.Xml.Linq;

namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests verifying the MainWindow.axaml layout structure after the UI layout rework.
/// These tests read the AXAML file as XML and verify structural properties.
///
/// **Validates: Requirements 1.1, 2.1, 2.3, 3.1**
/// </summary>
public class MainWindowLayoutStructureTests
{
    private static readonly string AxamlPath = Path.Combine(
        GetRepositoryRoot(), "NKDS.UI", "Views", "MainWindow.axaml");

    private static string GetRepositoryRoot()
    {
        // Walk up from the test assembly location to find the repository root
        string dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "NKDS.UI")) &&
                Directory.Exists(Path.Combine(dir, "NKitDataStore.Tests")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        // Fallback: use relative path from test output directory
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    private static string ReadAxamlContent()
    {
        Assert.True(File.Exists(AxamlPath), $"MainWindow.axaml not found at: {AxamlPath}");
        return File.ReadAllText(AxamlPath);
    }

    private static XDocument ParseAxaml()
    {
        string content = ReadAxamlContent();
        return XDocument.Parse(content);
    }

    [Fact]
    public void Layout_HasDockPanelWithContentControl()
    {
        // The layout uses a Panel > DockPanel with a ContentControl bound to ImageList
        XDocument doc = ParseAxaml();
        XNamespace ns = doc.Root!.GetDefaultNamespace();

        // DockPanel is inside a Panel wrapper (for resize grips overlay)
        XElement panel = doc.Root!.Elements(ns + "Panel").FirstOrDefault();
        Assert.NotNull(panel);

        XElement dockPanel = panel!.Elements(ns + "DockPanel").FirstOrDefault();
        Assert.NotNull(dockPanel);

        List<XElement> contentControls = dockPanel!.Elements(ns + "ContentControl").ToList();
        Assert.Single(contentControls);
    }

    [Fact]
    public void Layout_ContentControlBoundToImageList()
    {
        // The main content area is a ContentControl bound to the ImageList view model
        XDocument doc = ParseAxaml();
        XNamespace ns = doc.Root!.GetDefaultNamespace();

        XElement panel = doc.Root!.Elements(ns + "Panel").Single();
        XElement dockPanel = panel.Elements(ns + "DockPanel").Single();
        XElement contentControl = dockPanel.Elements(ns + "ContentControl").Single();

        string contentBinding = contentControl.Attribute("Content")?.Value;
        Assert.Equal("{Binding ImageList}", contentBinding);
    }

    [Fact]
    public void Layout_HasToolbarControl()
    {
        // The layout includes a ToolbarControl in the top dock area
        string content = ReadAxamlContent();
        Assert.Contains("ToolbarControl", content);
    }

    [Fact]
    public void Layout_NoSessionListPanelColumn()
    {
        string content = ReadAxamlContent();

        // Verify no Session_List_Panel reference exists in the layout
        Assert.DoesNotContain("Session_List_Panel", content);
        Assert.DoesNotContain("SessionListControl", content);
    }

    [Fact]
    public void Layout_NoThreeColumnGridDefinition()
    {
        string content = ReadAxamlContent();

        // The old three-column layout used ColumnDefinitions="240,*,Auto"
        Assert.DoesNotContain("240,*,Auto", content);
    }

    [Fact]
    public void Layout_NoRightSideComparisonColumnInMainGrid()
    {
        XDocument doc = ParseAxaml();
        XNamespace ns = doc.Root!.GetDefaultNamespace();

        // The main layout should be Panel > DockPanel (not a multi-column Grid)
        XElement panel = doc.Root!.Elements(ns + "Panel").FirstOrDefault();
        Assert.NotNull(panel);

        XElement rootDockPanel = panel!.Elements(ns + "DockPanel").FirstOrDefault();
        Assert.NotNull(rootDockPanel);

        // Verify no top-level Grid with column definitions containing the old right-side panel
        List<XElement> topLevelGrids = rootDockPanel.Elements(ns + "Grid")
            .Where(g => g.Attribute("ColumnDefinitions") != null)
            .ToList();

        // There should be no multi-column grid at the top level of the DockPanel
        foreach (XElement grid in topLevelGrids)
        {
            string colDefs = grid.Attribute("ColumnDefinitions")?.Value ?? "";
            Assert.DoesNotContain("Auto", colDefs);
        }
    }

    [Fact]
    public void Layout_NoIsRightPanelVisibleBinding()
    {
        string content = ReadAxamlContent();

        // Verify no IsRightPanelVisible binding exists anywhere in the AXAML
        Assert.DoesNotContain("IsRightPanelVisible", content);
    }
}