using System.Xml.Linq;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests verifying the ToolbarControl.axaml structure meets the icon-only
    /// button template requirements after the layout rework.
    ///
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    public class ToolbarControlAxamlStructureTests
    {
        private static readonly string AxamlPath = ResolveAxamlPath();

        private static string ResolveAxamlPath()
        {
            // Walk up from the test project directory to find the solution root,
            // then navigate to the NKDS.UI project's ToolbarControl.axaml
            string dir = TestDirectoryHelper.ProjectDirectory;
            while (dir != null)
            {
                string candidate = Path.Combine(dir, "NKDS.UI", "Views", "Controls", "ToolbarControl.axaml");
                if (File.Exists(candidate))
                    return candidate;

                // Check if we're at the solution root
                if (Directory.GetFiles(dir, "*.sln").Length > 0)
                {
                    candidate = Path.Combine(dir, "NKDS.UI", "Views", "Controls", "ToolbarControl.axaml");
                    if (File.Exists(candidate))
                        return candidate;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new FileNotFoundException(
                "Could not locate NKDS.UI/Views/Controls/ToolbarControl.axaml from test project directory.");
        }

        private static XDocument LoadAxaml()
        {
            string content = File.ReadAllText(AxamlPath);
            return XDocument.Parse(content);
        }

        [Fact]
        public void ButtonTemplate_HasCompactLabelTextBlock()
        {
            // Requirement 5.1: SVG_Button SHALL render as a compact icon button with a small label
            // The current design uses SVG icons with a small text label below (FontSize="11")
            XDocument doc = LoadAxaml();
            XNamespace avaloniaNamespace = "https://github.com/avaloniaui";

            List<XElement> dataTemplates = doc.Descendants(avaloniaNamespace + "DataTemplate").ToList();
            Assert.NotEmpty(dataTemplates);

            foreach (XElement template in dataTemplates)
            {
                List<XElement> textBlocks = template.Descendants(avaloniaNamespace + "TextBlock").ToList();
                // The template should have a compact label TextBlock
                Assert.NotEmpty(textBlocks);
                foreach (XElement tb in textBlocks)
                {
                    XAttribute fontSize = tb.Attribute("FontSize");
                    Assert.NotNull(fontSize);
                    // Font size should be small (compact label, not a large card-style label)
                    Assert.True(double.Parse(fontSize!.Value) <= 12,
                        $"TextBlock FontSize {fontSize.Value} should be <= 12 for compact layout");
                }
            }
        }

        [Fact]
        public void ButtonTemplate_SvgDimensionsSmallerThanOldCardLayout()
        {
            // Requirement 5.3: SVG_Button SHALL have dimensions smaller than the previous 115×75px card-style layout
            // The current design uses Svg elements (not Image) with 28×28 dimensions
            XDocument doc = LoadAxaml();
            XNamespace avaloniaNamespace = "https://github.com/avaloniaui";

            List<XElement> svgs = doc.Descendants(avaloniaNamespace + "Svg").ToList();
            Assert.NotEmpty(svgs);

            foreach (XElement svg in svgs)
            {
                XAttribute widthAttr = svg.Attribute("Width");
                XAttribute heightAttr = svg.Attribute("Height");

                Assert.NotNull(widthAttr);
                Assert.NotNull(heightAttr);

                double width = double.Parse(widthAttr!.Value);
                double height = double.Parse(heightAttr!.Value);

                // Must be smaller than the old 115×75 card-style layout
                Assert.True(width < 115, $"Svg width {width} should be less than 115");
                Assert.True(height < 75, $"Svg height {height} should be less than 75");
            }
        }

        [Fact]
        public void ButtonTemplate_HasTooltipBinding()
        {
            // Requirement 5.2: SVG_Button SHALL display a tooltip containing the operation name
            string content = File.ReadAllText(AxamlPath);

            // Verify that ToolTip.Tip="{Binding Tooltip}" is present on a Button element
            Assert.Contains("ToolTip.Tip=\"{Binding Tooltip}\"", content);
        }

        [Fact]
        public void ButtonTemplate_NoBorderWrapper()
        {
            // Verify the old 115×75 Border wrapper is not present in the DataTemplate
            XDocument doc = LoadAxaml();
            XNamespace avaloniaNamespace = "https://github.com/avaloniaui";

            List<XElement> dataTemplates = doc.Descendants(avaloniaNamespace + "DataTemplate").ToList();
            Assert.NotEmpty(dataTemplates);

            foreach (XElement template in dataTemplates)
            {
                // The DataTemplate should not contain a Border with Width/Height attributes
                // (the old card-style had a Border with specific dimensions)
                List<XElement> borders = template.Descendants(avaloniaNamespace + "Border").ToList();
                foreach (XElement border in borders)
                {
                    XAttribute widthAttr = border.Attribute("Width");
                    XAttribute heightAttr = border.Attribute("Height");

                    // If a border exists in the template, it should not have the old 115×75 dimensions
                    if (widthAttr != null && heightAttr != null)
                    {
                        double width = double.Parse(widthAttr.Value);
                        double height = double.Parse(heightAttr.Value);
                        Assert.True(width < 115 || height < 75,
                            $"Found Border with dimensions {width}×{height} which matches the old card-style layout");
                    }
                }
            }
        }
    }
}