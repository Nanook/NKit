using System.Xml.Linq;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for Toolbar Row 2 contextual actions in MainWindow.axaml.
    /// Verifies that stats progress, cancel button, and show graphs button
    /// have correct visibility bindings.
    ///
    /// **Validates: Requirements 4.4, 4.5, 6.3, 6.4**
    /// </summary>
    public class ToolbarRow2ContextualActionsTests
    {
        private static readonly string AxamlPath = Path.Combine(
            FindRepoRoot(), "NkdsUi", "Views", "MainWindow.axaml");

        private readonly XDocument _doc;
        private readonly XNamespace _ns = "https://github.com/avaloniaui";

        public ToolbarRow2ContextualActionsTests()
        {
            Assert.True(File.Exists(AxamlPath), $"MainWindow.axaml not found at: {AxamlPath}");
            _doc = XDocument.Load(AxamlPath);
        }

        [Fact]
        public void StatsProgress_TextBlock_IsVisible_BoundToIsCalculating()
        {
            // Requirement 4.4: Stats progress visible when calculating
            var textBlocks = _doc.Descendants(_ns + "TextBlock");
            var statsProgress = textBlocks.FirstOrDefault(el =>
                el.Attribute("IsVisible")?.Value == "{Binding StatsCalculation.IsCalculating}");

            Assert.NotNull(statsProgress);
            Assert.Equal("{Binding StatsCalculation.ProgressText}", statsProgress.Attribute("Text")?.Value);
        }

        [Fact]
        public void CancelButton_IsVisible_BoundToIsCalculating()
        {
            // Requirement 6.3: Cancel button visible when stats calculation is active
            var buttons = _doc.Descendants(_ns + "Button");
            var cancelButton = buttons.FirstOrDefault(el =>
                el.Attribute("Command")?.Value == "{Binding StatsCalculation.CancelStatsCommand}" &&
                el.Attribute("IsVisible")?.Value == "{Binding StatsCalculation.IsCalculating}");

            Assert.NotNull(cancelButton);
        }

        [Fact]
        public void ShowGraphsButton_IsVisible_BoundToHasComputedStats()
        {
            // Requirement 6.4: Show Graphs button visible after stats computed
            var buttons = _doc.Descendants(_ns + "Button");
            var showGraphsButton = buttons.FirstOrDefault(el =>
                el.Attribute("Command")?.Value == "{Binding StatsCalculation.ShowGraphsCommand}" &&
                el.Attribute("IsVisible")?.Value == "{Binding StatsCalculation.HasComputedStats}");

            Assert.NotNull(showGraphsButton);
        }

        private static string FindRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir, ".git")) ||
                    File.Exists(Path.Combine(dir, "NKit.sln")))
                    return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }
            // Fallback: walk up from test assembly location
            dir = AppContext.BaseDirectory;
            for (int i = 0; i < 6; i++)
                dir = Directory.GetParent(dir!)?.FullName;
            return dir ?? throw new InvalidOperationException("Cannot find repository root");
        }
    }
}
