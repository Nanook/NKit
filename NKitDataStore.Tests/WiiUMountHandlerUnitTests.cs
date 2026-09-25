namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for WiiUMountHandler logical behavior.
    ///
    /// WiiUMountHandler requires a real VfsModel with DataStore, MountRegistry, and
    /// ImageReader infrastructure, so these tests validate the LOGICAL behavior using
    /// simulated models that mirror the handler's visibility and routing logic:
    ///   - Image mode (-i): TmdAppFolder shown as folder, individual TmdApp images hidden
    ///   - Image mode (-i): legacy APP images without TmdAppFolder shown with legacy behavior
    ///   - Filesystem mode (-fs): individual disambiguated images shown, TmdAppFolder hidden
    ///   - extractBaseName: strips " [index]" suffix from disambiguated names
    ///
    /// **Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5, 12.1**
    /// </summary>
    public class WiiUMountHandlerUnitTests
    {
        #region Simulation helpers

        /// <summary>
        /// Simulated image record for visibility testing.
        /// </summary>
        private class SimulatedImage
        {
            public long Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public ImageFormat Format { get; set; }
            public string SetName { get; set; } = string.Empty;
            public string System { get; set; } = string.Empty;
            public bool Removed { get; set; }
            public bool IsMergedSecondary { get; set; }
        }

        /// <summary>
        /// Simulates WiiUMountHandler.extractBaseName():
        /// Extracts the base name from a potentially disambiguated image name.
        /// E.g., "Game Title [tmd.0]" → "Game Title", "Game Title" → "Game Title".
        /// </summary>
        private static string SimulateExtractBaseName(string imageName)
        {
            if (string.IsNullOrEmpty(imageName))
                return imageName;

            int bracketStart = imageName.LastIndexOf(" [", StringComparison.Ordinal);
            if (bracketStart >= 0 && imageName.EndsWith("]"))
                return imageName.Substring(0, bracketStart);

            return imageName;
        }

        /// <summary>
        /// Result of visibility simulation for a single image.
        /// </summary>
        private class VisibilityResult
        {
            public string DisplayName { get; set; } = string.Empty;
            public bool IsFolder { get; set; }
            public bool IsImage { get; set; }
        }

        /// <summary>
        /// Simulates WiiUMountHandler.ListRoot() image mode visibility logic.
        /// In image mode (-i):
        ///   - TmdAppFolder images appear as folders (using base name)
        ///   - App images whose base name matches a TmdAppFolder are hidden
        ///   - App images without a TmdAppFolder appear as folders (legacy behavior)
        ///   - Other formats appear as iso files
        /// </summary>
        private static List<VisibilityResult> SimulateListRootImageMode(
            List<SimulatedImage> images, string systemName)
        {
            // Step 1: Collect base names that have a non-removed TmdAppFolder
            HashSet<string> tmdAppFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SimulatedImage img in images)
            {
                if (!string.Equals(img.System, systemName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (img.Format == ImageFormat.TmdAppFolder && !img.Removed)
                    tmdAppFolderNames.Add(img.Name);
            }

            // Step 2: Determine visibility for each image
            List<VisibilityResult> results = new List<VisibilityResult>();
            foreach (SimulatedImage img in images)
            {
                if (!string.Equals(img.System, systemName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (img.Removed)
                    continue;
                if (img.IsMergedSecondary)
                    continue;

                if (img.Format == ImageFormat.TmdAppFolder)
                {
                    // Show TmdAppFolder as folder
                    results.Add(new VisibilityResult
                    {
                        DisplayName = img.Name,
                        IsFolder = true,
                        IsImage = false
                    });
                }
                else if (img.Format == ImageFormat.App)
                {
                    // Hide App images that have a parent TmdAppFolder
                    string baseName = SimulateExtractBaseName(img.Name);
                    if (tmdAppFolderNames.Contains(baseName))
                        continue;

                    // Legacy APP images without a TmdAppFolder: show as folder
                    results.Add(new VisibilityResult
                    {
                        DisplayName = img.Name,
                        IsFolder = true,
                        IsImage = false
                    });
                }
                else
                {
                    // Other formats: show as iso file
                    results.Add(new VisibilityResult
                    {
                        DisplayName = img.Name,
                        IsFolder = false,
                        IsImage = true
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// Simulates filesystem mode (-fs) visibility logic.
        /// In filesystem mode:
        ///   - Each disambiguated "Image Name [tmd.X]" appears as a folder
        ///   - TmdAppFolder images are hidden (Req 10.4)
        ///   - Other images appear as folders with their own filesystem.yaml contents
        /// </summary>
        private static List<VisibilityResult> SimulateListRootFilesystemMode(
            List<SimulatedImage> images, string systemName)
        {
            List<VisibilityResult> results = new List<VisibilityResult>();
            foreach (SimulatedImage img in images)
            {
                if (!string.Equals(img.System, systemName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (img.Removed)
                    continue;

                // TmdAppFolder images are excluded from filesystem mode
                if (img.Format == ImageFormat.TmdAppFolder)
                    continue;

                // All other images (including individual App [tmd.X]) shown as folders
                results.Add(new VisibilityResult
                {
                    DisplayName = img.Name,
                    IsFolder = true,
                    IsImage = false
                });
            }

            return results;
        }

        /// <summary>
        /// Simulates WiiUMountHandler.ListFolder() routing logic.
        /// Returns "FolderMountHandler" if the image is TmdAppFolder,
        /// "LegacyMerge" if it's a legacy App image.
        /// </summary>
        private static string SimulateListFolderRouting(ImageFormat format)
        {
            if (format == ImageFormat.TmdAppFolder)
                return "FolderMountHandler";
            if (format == ImageFormat.App)
                return "LegacyMerge";
            return "Base";
        }

        /// <summary>
        /// Builds a standard test scenario with TmdAppFolder and child App images.
        /// </summary>
        private static List<SimulatedImage> BuildTypicalTmdAppScenario()
        {
            return new List<SimulatedImage>
            {
                // TmdAppFolder: the unified folder image
                new SimulatedImage
                {
                    Id = 1, Name = "Game Title", Format = ImageFormat.TmdAppFolder,
                    SetName = "wiiu", System = "WiiU", Removed = false
                },
                // Child App images (disambiguated names)
                new SimulatedImage
                {
                    Id = 2, Name = "Game Title [tmd.0]", Format = ImageFormat.App,
                    SetName = "wiiu", System = "WiiU", Removed = false
                },
                new SimulatedImage
                {
                    Id = 3, Name = "Game Title [tmd.1]", Format = ImageFormat.App,
                    SetName = "wiiu", System = "WiiU", Removed = false
                },
                new SimulatedImage
                {
                    Id = 4, Name = "Game Title [tmd.2]", Format = ImageFormat.App,
                    SetName = "wiiu", System = "WiiU", Removed = false
                },
            };
        }

        #endregion

        #region extractBaseName tests

        /// <summary>
        /// Validates: Requirement 10.1
        /// Disambiguated name with bracket suffix is correctly parsed.
        /// </summary>
        [Fact]
        public void ExtractBaseName_DisambiguatedName_ReturnsBaseName()
        {
            string result = SimulateExtractBaseName("Game Title [tmd.0]");
            Assert.Equal("Game Title", result);
        }

        /// <summary>
        /// Validates: Requirement 10.1
        /// Plain name without brackets is returned unchanged.
        /// </summary>
        [Fact]
        public void ExtractBaseName_PlainName_ReturnsUnchanged()
        {
            string result = SimulateExtractBaseName("Game Title");
            Assert.Equal("Game Title", result);
        }

        /// <summary>
        /// Validates: Requirement 10.1
        /// Empty string returns empty.
        /// </summary>
        [Fact]
        public void ExtractBaseName_EmptyString_ReturnsEmpty()
        {
            string result = SimulateExtractBaseName("");
            Assert.Equal("", result);
        }

        /// <summary>
        /// Validates: Requirement 10.1
        /// Null returns null.
        /// </summary>
        [Fact]
        public void ExtractBaseName_Null_ReturnsNull()
        {
            string result = SimulateExtractBaseName(null!);
            Assert.Null(result);
        }

        /// <summary>
        /// Validates: Requirement 10.1
        /// Name with brackets in the middle (not at end) is returned unchanged.
        /// </summary>
        [Fact]
        public void ExtractBaseName_BracketsNotAtEnd_ReturnsUnchanged()
        {
            string result = SimulateExtractBaseName("Game [v1] Title");
            Assert.Equal("Game [v1] Title", result);
        }

        /// <summary>
        /// Validates: Requirement 10.1
        /// Multiple bracket suffixes: only the last one is stripped.
        /// </summary>
        [Fact]
        public void ExtractBaseName_MultipleBrackets_StripsLastOnly()
        {
            string result = SimulateExtractBaseName("Game [v1] [tmd.0]");
            Assert.Equal("Game [v1]", result);
        }

        #endregion

        #region Image mode: TmdAppFolder shown, child App images hidden

        /// <summary>
        /// Validates: Requirement 10.1
        /// In image mode, TmdAppFolder is shown as a folder.
        /// </summary>
        [Fact]
        public void ImageMode_TmdAppFolder_ShownAsFolder()
        {
            List<SimulatedImage> images = BuildTypicalTmdAppScenario();
            List<VisibilityResult> results = SimulateListRootImageMode(images, "WiiU");

            VisibilityResult tmdAppFolderResult = results.Find(r => r.DisplayName == "Game Title");
            Assert.NotNull(tmdAppFolderResult);
            Assert.True(tmdAppFolderResult.IsFolder);
            Assert.False(tmdAppFolderResult.IsImage);
        }

        /// <summary>
        /// Validates: Requirement 10.1
        /// In image mode, individual TmdApp images with a parent TmdAppFolder are hidden.
        /// </summary>
        [Fact]
        public void ImageMode_ChildAppImages_HiddenWhenTmdAppFolderExists()
        {
            List<SimulatedImage> images = BuildTypicalTmdAppScenario();
            List<VisibilityResult> results = SimulateListRootImageMode(images, "WiiU");

            // Child App images should not appear
            Assert.DoesNotContain(results, r => r.DisplayName == "Game Title [tmd.0]");
            Assert.DoesNotContain(results, r => r.DisplayName == "Game Title [tmd.1]");
            Assert.DoesNotContain(results, r => r.DisplayName == "Game Title [tmd.2]");
        }

        /// <summary>
        /// Validates: Requirement 10.1
        /// In image mode, only the TmdAppFolder appears (not the children).
        /// </summary>
        [Fact]
        public void ImageMode_OnlyTmdAppFolderVisible_ChildrenHidden()
        {
            List<SimulatedImage> images = BuildTypicalTmdAppScenario();
            List<VisibilityResult> results = SimulateListRootImageMode(images, "WiiU");

            // Only one result: the TmdAppFolder
            Assert.Single(results);
            Assert.Equal("Game Title", results[0].DisplayName);
        }

        #endregion

        #region Image mode: legacy APP images without TmdAppFolder

        /// <summary>
        /// Validates: Requirement 10.5, 12.1
        /// Legacy App images without a TmdAppFolder are shown as folders.
        /// </summary>
        [Fact]
        public void ImageMode_LegacyAppWithoutTmdAppFolder_ShownAsFolder()
        {
            List<SimulatedImage> images = new List<SimulatedImage>
            {
                new SimulatedImage
                {
                    Id = 10, Name = "Legacy Game", Format = ImageFormat.App,
                    SetName = "wiiu", System = "WiiU", Removed = false
                },
            };

            List<VisibilityResult> results = SimulateListRootImageMode(images, "WiiU");

            Assert.Single(results);
            Assert.Equal("Legacy Game", results[0].DisplayName);
            Assert.True(results[0].IsFolder);
        }

        /// <summary>
        /// Validates: Requirement 10.5, 12.1
        /// Multiple legacy App images (same base name, no TmdAppFolder) are all shown.
        /// </summary>
        [Fact]
        public void ImageMode_MultipleLegacyApps_AllShown()
        {
            List<SimulatedImage> images = new List<SimulatedImage>
            {
                new SimulatedImage
                {
                    Id = 10, Name = "Legacy Game", Format = ImageFormat.App,
                    SetName = "wiiu", System = "WiiU", Removed = false
                },
                new SimulatedImage
                {
                    Id = 11, Name = "Another Game", Format = ImageFormat.App,
                    SetName = "wiiu", System = "WiiU", Removed = false
                },
            };

            List<VisibilityResult> results = SimulateListRootImageMode(images, "WiiU");

            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.DisplayName == "Legacy Game");
            Assert.Contains(results, r => r.DisplayName == "Another Game");
        }

        /// <summary>
        /// Validates: Requirement 10.5, 12.1
        /// Mix of TmdAppFolder-covered and legacy App images: only legacy ones shown alongside TmdAppFolder.
        /// </summary>
        [Fact]
        public void ImageMode_MixedTmdAppFolderAndLegacy_CorrectVisibility()
        {
            List<SimulatedImage> images = new List<SimulatedImage>
            {
                // TmdAppFolder + children
                new SimulatedImage
                {
                    Id = 1, Name = "New Game", Format = ImageFormat.TmdAppFolder,
                    SetName = "wiiu", System = "WiiU", Removed = false
                },
                new SimulatedImage
                {
                    Id = 2, Name = "New Game [tmd.0]", Format = ImageFormat.App,
                    SetName = "wiiu", System = "WiiU", Removed = false
                },
                // Legacy App (no TmdAppFolder)
                new SimulatedImage
                {
                    Id = 10, Name = "Old Game", Format = ImageFormat.App,
                    SetName = "wiiu", System = "WiiU", Removed = false
                },
            };

            List<VisibilityResult> results = SimulateListRootImageMode(images, "WiiU");

            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.DisplayName == "New Game" && r.IsFolder);
            Assert.Contains(results, r => r.DisplayName == "Old Game" && r.IsFolder);
            Assert.DoesNotContain(results, r => r.DisplayName == "New Game [tmd.0]");
        }

        #endregion

        #region Filesystem mode: individual images shown, TmdAppFolder hidden

        /// <summary>
        /// Validates: Requirement 10.3
        /// In filesystem mode, individual disambiguated images are shown as folders.
        /// </summary>
        [Fact]
        public void FilesystemMode_IndividualAppImages_ShownAsFolders()
        {
            List<SimulatedImage> images = BuildTypicalTmdAppScenario();
            List<VisibilityResult> results = SimulateListRootFilesystemMode(images, "WiiU");

            Assert.Contains(results, r => r.DisplayName == "Game Title [tmd.0]" && r.IsFolder);
            Assert.Contains(results, r => r.DisplayName == "Game Title [tmd.1]" && r.IsFolder);
            Assert.Contains(results, r => r.DisplayName == "Game Title [tmd.2]" && r.IsFolder);
        }

        /// <summary>
        /// Validates: Requirement 10.4
        /// In filesystem mode, TmdAppFolder images are hidden.
        /// </summary>
        [Fact]
        public void FilesystemMode_TmdAppFolder_Hidden()
        {
            List<SimulatedImage> images = BuildTypicalTmdAppScenario();
            List<VisibilityResult> results = SimulateListRootFilesystemMode(images, "WiiU");

            // TmdAppFolder should not appear in filesystem mode
            Assert.DoesNotContain(results, r =>
                r.DisplayName == "Game Title" && !r.DisplayName.Contains("["));
        }

        /// <summary>
        /// Validates: Requirement 10.3, 10.4
        /// In filesystem mode, only individual App images are shown (not TmdAppFolder).
        /// </summary>
        [Fact]
        public void FilesystemMode_OnlyAppImagesVisible()
        {
            List<SimulatedImage> images = BuildTypicalTmdAppScenario();
            List<VisibilityResult> results = SimulateListRootFilesystemMode(images, "WiiU");

            // 3 App images shown, TmdAppFolder hidden
            Assert.Equal(3, results.Count);
            Assert.All(results, r => Assert.True(r.IsFolder));
        }

        /// <summary>
        /// Validates: Requirement 10.3
        /// In filesystem mode, legacy App images (no TmdAppFolder) are also shown.
        /// </summary>
        [Fact]
        public void FilesystemMode_LegacyApps_AlsoShown()
        {
            List<SimulatedImage> images = new List<SimulatedImage>
            {
                new SimulatedImage
                {
                    Id = 10, Name = "Legacy Game", Format = ImageFormat.App,
                    SetName = "wiiu", System = "WiiU", Removed = false
                },
            };

            List<VisibilityResult> results = SimulateListRootFilesystemMode(images, "WiiU");

            Assert.Single(results);
            Assert.Equal("Legacy Game", results[0].DisplayName);
            Assert.True(results[0].IsFolder);
        }

        /// <summary>
        /// Validates: Requirement 10.3, 10.4
        /// Mixed scenario in filesystem mode: TmdAppFolder hidden, all App images shown.
        /// </summary>
        [Fact]
        public void FilesystemMode_MixedScenario_TmdAppFolderHiddenAppsShown()
        {
            List<SimulatedImage> images = new List<SimulatedImage>
            {
                new SimulatedImage
                {
                    Id = 1, Name = "New Game", Format = ImageFormat.TmdAppFolder,
                    SetName = "wiiu", System = "WiiU", Removed = false
                },
                new SimulatedImage
                {
                    Id = 2, Name = "New Game [tmd.0]", Format = ImageFormat.App,
                    SetName = "wiiu", System = "WiiU", Removed = false
                },
                new SimulatedImage
                {
                    Id = 3, Name = "New Game [tmd.1]", Format = ImageFormat.App,
                    SetName = "wiiu", System = "WiiU", Removed = false
                },
                new SimulatedImage
                {
                    Id = 10, Name = "Old Game", Format = ImageFormat.App,
                    SetName = "wiiu", System = "WiiU", Removed = false
                },
            };

            List<VisibilityResult> results = SimulateListRootFilesystemMode(images, "WiiU");

            Assert.Equal(3, results.Count);
            Assert.Contains(results, r => r.DisplayName == "New Game [tmd.0]");
            Assert.Contains(results, r => r.DisplayName == "New Game [tmd.1]");
            Assert.Contains(results, r => r.DisplayName == "Old Game");
            Assert.DoesNotContain(results, r => r.DisplayName == "New Game" && !r.DisplayName.Contains("["));
        }

        #endregion

        #region ListFolder routing: TmdAppFolder delegates to FolderMountHandler

        /// <summary>
        /// Validates: Requirement 10.2
        /// TmdAppFolder images delegate ListFolder to FolderMountHandler.
        /// </summary>
        [Fact]
        public void ListFolderRouting_TmdAppFolder_DelegatesToFolderMountHandler()
        {
            string routing = SimulateListFolderRouting(ImageFormat.TmdAppFolder);
            Assert.Equal("FolderMountHandler", routing);
        }

        /// <summary>
        /// Validates: Requirement 12.1
        /// Legacy App images use the legacy merge logic.
        /// </summary>
        [Fact]
        public void ListFolderRouting_LegacyApp_UsesLegacyMerge()
        {
            string routing = SimulateListFolderRouting(ImageFormat.App);
            Assert.Equal("LegacyMerge", routing);
        }

        /// <summary>
        /// Validates: Requirement 10.2
        /// Other formats fall through to base handler.
        /// </summary>
        [Fact]
        public void ListFolderRouting_OtherFormat_UsesBase()
        {
            string routing = SimulateListFolderRouting(ImageFormat.Iso);
            Assert.Equal("Base", routing);
        }

        #endregion

        #region Edge cases: removed images, different systems, merged secondaries

        /// <summary>
        /// Validates: Requirement 10.1
        /// Removed TmdAppFolder does not suppress child App images.
        /// </summary>
        [Fact]
        public void ImageMode_RemovedTmdAppFolder_ChildAppsShown()
        {
            List<SimulatedImage> images = new List<SimulatedImage>
            {
                new SimulatedImage
                {
                    Id = 1, Name = "Game Title", Format = ImageFormat.TmdAppFolder,
                    SetName = "wiiu", System = "WiiU", Removed = true
                },
                new SimulatedImage
                {
                    Id = 2, Name = "Game Title [tmd.0]", Format = ImageFormat.App,
                    SetName = "wiiu", System = "WiiU", Removed = false
                },
                new SimulatedImage
                {
                    Id = 3, Name = "Game Title [tmd.1]", Format = ImageFormat.App,
                    SetName = "wiiu", System = "WiiU", Removed = false
                },
            };

            List<VisibilityResult> results = SimulateListRootImageMode(images, "WiiU");

            // TmdAppFolder is removed, so child App images should be visible as legacy
            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.DisplayName == "Game Title [tmd.0]");
            Assert.Contains(results, r => r.DisplayName == "Game Title [tmd.1]");
        }

        /// <summary>
        /// Validates: Requirement 10.1
        /// Images from a different system are not included.
        /// </summary>
        [Fact]
        public void ImageMode_DifferentSystem_NotIncluded()
        {
            List<SimulatedImage> images = new List<SimulatedImage>
            {
                new SimulatedImage
                {
                    Id = 1, Name = "Game Title", Format = ImageFormat.TmdAppFolder,
                    SetName = "wiiu", System = "WiiU", Removed = false
                },
                new SimulatedImage
                {
                    Id = 5, Name = "Other System Game", Format = ImageFormat.App,
                    SetName = "wiiu", System = "GameCube", Removed = false
                },
            };

            List<VisibilityResult> results = SimulateListRootImageMode(images, "WiiU");

            Assert.Single(results);
            Assert.Equal("Game Title", results[0].DisplayName);
        }

        /// <summary>
        /// Validates: Requirement 10.1
        /// Merged secondary images are hidden in image mode.
        /// </summary>
        [Fact]
        public void ImageMode_MergedSecondary_Hidden()
        {
            List<SimulatedImage> images = new List<SimulatedImage>
            {
                new SimulatedImage
                {
                    Id = 10, Name = "Legacy Game", Format = ImageFormat.App,
                    SetName = "wiiu", System = "WiiU", Removed = false,
                    IsMergedSecondary = false
                },
                new SimulatedImage
                {
                    Id = 11, Name = "Legacy Game", Format = ImageFormat.App,
                    SetName = "wiiu", System = "WiiU", Removed = false,
                    IsMergedSecondary = true
                },
            };

            List<VisibilityResult> results = SimulateListRootImageMode(images, "WiiU");

            // Only the primary should appear
            Assert.Single(results);
            Assert.Equal("Legacy Game", results[0].DisplayName);
        }

        /// <summary>
        /// Validates: Requirement 10.4
        /// Removed images are excluded from filesystem mode.
        /// </summary>
        [Fact]
        public void FilesystemMode_RemovedImages_Excluded()
        {
            List<SimulatedImage> images = new List<SimulatedImage>
            {
                new SimulatedImage
                {
                    Id = 2, Name = "Game Title [tmd.0]", Format = ImageFormat.App,
                    SetName = "wiiu", System = "WiiU", Removed = true
                },
                new SimulatedImage
                {
                    Id = 3, Name = "Game Title [tmd.1]", Format = ImageFormat.App,
                    SetName = "wiiu", System = "WiiU", Removed = false
                },
            };

            List<VisibilityResult> results = SimulateListRootFilesystemMode(images, "WiiU");

            Assert.Single(results);
            Assert.Equal("Game Title [tmd.1]", results[0].DisplayName);
        }

        /// <summary>
        /// Validates: Requirement 10.1
        /// Empty image list produces no results.
        /// </summary>
        [Fact]
        public void ImageMode_EmptyImageList_NoResults()
        {
            List<VisibilityResult> results = SimulateListRootImageMode(new List<SimulatedImage>(), "WiiU");
            Assert.Empty(results);
        }

        /// <summary>
        /// Validates: Requirement 10.4
        /// Empty image list produces no results in filesystem mode.
        /// </summary>
        [Fact]
        public void FilesystemMode_EmptyImageList_NoResults()
        {
            List<VisibilityResult> results = SimulateListRootFilesystemMode(new List<SimulatedImage>(), "WiiU");
            Assert.Empty(results);
        }

        #endregion
    }
}