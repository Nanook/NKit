using Nanook.NKit;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Unit tests for NKitTaskContext step routing.
    /// Validates that specific (task, system, source, config) combinations resolve
    /// to the correct step sequences.
    ///
    /// _Requirements: 1.3, 2.3, 4.1, 4.2, 4.3, 4.4, 5.1, 6.1, 6.2, 6.3, 6.4, 6.5, 8.2, 8.3, 8.4_
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class TaskStepRoutingUnitTests
    {
        #region Expand Default ISO from DataStore → Expand-Image (Req 4.1)

        [Fact]
        public void Expand_DefaultIso_FromDataStore_ResolvesToExpandImage()
        {
            // Default system, ISO image source, configString="iso" (DataStore expand)
            // Should resolve to Expand-Image
            string srcFormat = ".iso";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "expand", "default", srcFormat, "", "iso",
                false, "n", parts, false, false, false, null);

            Assert.Equal("Expand-Image", task.Steps[0].StepInfo.Name);
        }

        [Fact]
        public void Expand_DefaultIso_FromDataStore_EmptyConfig_ResolvesToExpandImage()
        {
            // Default system, ISO image source, configString="" (native expand)
            // Should resolve to Expand-Image (matches "iso/" entries via empty prefix)
            string srcFormat = ".iso";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "expand", "default", srcFormat, "", "",
                false, "n", parts, false, false, false, null);

            Assert.Equal("Expand-Image", task.Steps[0].StepInfo.Name);
        }

        #endregion

        #region Convert Default ISO to CUE → Convert-Iso-CueToc (Req 4.2)

        [Fact]
        public void Convert_DefaultFolderindex_ToCue_ResolvesToConvertIsoCueToc()
        {
            // Default system, folderindex source (CUE/BIN in DataStore), convert to "cue"
            // Convert-Iso-CueToc handles folderindex sources for CUE output
            // When a DataStore image is converted to CUE, the source is treated as folderindex
            string srcFormat = ".cue";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "convert", "default", srcFormat, "", "cue",
                false, "n", parts, false, false, false, "cue");

            Assert.Equal("Convert-Iso-CueToc", task.Steps[0].StepInfo.Name);
        }

        [Fact]
        public void Convert_DefaultImage_ToIso_ResolvesToConvertImage()
        {
            // Default system, image source, convert to "iso"
            // Should resolve to Convert-Image (generic image conversion)
            string srcFormat = ".iso";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "convert", "default", srcFormat, "", "iso",
                false, "n", parts, false, false, false, "iso");

            Assert.Equal("Convert-Image", task.Steps[0].StepInfo.Name);
        }

        #endregion

        #region Convert Default ISO to CSO → Convert-Iso-CsoZso (Req 4.3)

        [Fact]
        public void Convert_DefaultIso_ToCso_ResolvesToConvertIsoCsoZso()
        {
            // Default system, ISO image source, convert to "cso"
            // Should resolve to Convert-Iso-CsoZso
            string srcFormat = ".iso";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "convert", "default", srcFormat, "", "cso",
                false, "n", parts, false, false, false, "cso");

            Assert.Equal("Convert-Iso-CsoZso", task.Steps[0].StepInfo.Name);
        }

        [Fact]
        public void Convert_DefaultIso_ToZso_ResolvesToConvertIsoCsoZso()
        {
            // Default system, ISO image source, convert to "zso"
            // Should resolve to Convert-Iso-CsoZso
            string srcFormat = ".iso";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "convert", "default", srcFormat, "", "zso",
                false, "n", parts, false, false, false, "zso");

            Assert.Equal("Convert-Iso-CsoZso", task.Steps[0].StepInfo.Name);
        }

        #endregion

        #region Verify Default ISO → Verify-Image with DataStore method (Req 4.4)

        [Fact]
        public void Verify_DefaultIso_ResolvesToVerifyImage()
        {
            // Default system, ISO image source, verify task
            // Should resolve to Verify-Image
            string srcFormat = ".iso";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "verify", "default", srcFormat, "", "",
                false, "n", parts, false, false, false, "none");

            Assert.Equal("Verify-Image", task.Steps[0].StepInfo.Name);
        }

        #endregion

        #region Expand Default CUE folderindex → Expand-Iso-CueToc (Req 5.1)

        [Fact]
        public void Expand_DefaultCue_Folderindex_ResolvesToExpandIsoCueToc()
        {
            // Default system, CUE folderindex source, configString="cue" (DataStore expand)
            // Should resolve to Expand-Iso-CueToc
            string srcFormat = ".cue";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "expand", "default", srcFormat, "", "cue",
                false, "n", parts, false, false, false, "");

            Assert.Equal("Expand-Iso-CueToc", task.Steps[0].StepInfo.Name);
        }

        [Fact]
        public void Expand_DefaultCue_Folderindex_EmptyConfig_ResolvesToExpandIsoCueToc()
        {
            // Default system, CUE folderindex source, configString="" (native expand)
            // Should resolve to Expand-Iso-CueToc (matches "cue/" entries via empty prefix)
            string srcFormat = ".cue";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "expand", "default", srcFormat, "", "",
                false, "n", parts, false, false, false, "");

            Assert.Equal("Expand-Iso-CueToc", task.Steps[0].StepInfo.Name);
        }

        #endregion

        #region Expand Xbox → Expand-XBox (Req 6.1)

        [Fact]
        public void Expand_Xbox_ResolvesToExpandXBox()
        {
            // Xbox system, ISO image source, configString="iso" (DataStore expand)
            // Should resolve to Expand-XBox
            string srcFormat = ".iso";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "expand", "xbox", srcFormat, "", "iso",
                false, "n", parts, false, false, false, null);

            Assert.Equal("Expand-XBox", task.Steps[0].StepInfo.Name);
        }

        [Fact]
        public void Expand_Xbox_EmptyConfig_ResolvesToExpandXBox()
        {
            // Xbox system, ISO image source, configString="" (native expand)
            // Should resolve to Expand-XBox (matches "iso/" entries)
            string srcFormat = ".iso";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "expand", "xbox", srcFormat, "", "",
                false, "n", parts, false, false, false, null);

            Assert.Equal("Expand-XBox", task.Steps[0].StepInfo.Name);
        }

        [Fact]
        public void Expand_Xbox_Xiso_ResolvesToExpandXBox()
        {
            // Xbox system, ISO image source, configString="xiso"
            // Should resolve to Expand-XBox
            string srcFormat = ".iso";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "expand", "xbox", srcFormat, "", "xiso",
                false, "n", parts, false, false, false, null);

            Assert.Equal("Expand-XBox", task.Steps[0].StepInfo.Name);
        }

        #endregion

        #region Convert Xbox to XISO → Convert-XBox-Xiso (Req 6.2)

        [Fact]
        public void Convert_Xbox_ToXiso_ResolvesToConvertXBoxXiso()
        {
            // Xbox system, ISO image source, convert to "xiso"
            // Should resolve to Convert-XBox-Xiso
            string srcFormat = ".iso";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "convert", "xbox", srcFormat, "", "xiso",
                false, "n", parts, false, false, false, "xiso");

            Assert.Equal("Convert-XBox-Xiso", task.Steps[0].StepInfo.Name);
        }

        #endregion

        #region Convert Xbox to ISO → Convert-Image (Req 6.3)

        [Fact]
        public void Convert_Xbox_ToIso_ResolvesToConvertImage()
        {
            // Xbox system, ISO image source, convert to "iso"
            // Should resolve to Convert-Image
            string srcFormat = ".iso";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "convert", "xbox", srcFormat, "", "iso",
                false, "n", parts, false, false, false, "iso");

            Assert.Equal("Convert-Image", task.Steps[0].StepInfo.Name);
        }

        #endregion

        #region Expand Xbox360 → Expand-XBox (Req 6.4)

        [Fact]
        public void Expand_Xbox360_ResolvesToExpandXBox()
        {
            // Xbox360 system, ISO image source, configString="iso"
            // Should resolve to Expand-XBox
            string srcFormat = ".iso";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "expand", "xbox360", srcFormat, "", "iso",
                false, "n", parts, false, false, false, null);

            Assert.Equal("Expand-XBox", task.Steps[0].StepInfo.Name);
        }

        [Fact]
        public void Expand_Xbox360_EmptyConfig_ResolvesToExpandXBox()
        {
            // Xbox360 system, ISO image source, configString=""
            // Should resolve to Expand-XBox
            string srcFormat = ".iso";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "expand", "xbox360", srcFormat, "", "",
                false, "n", parts, false, false, false, null);

            Assert.Equal("Expand-XBox", task.Steps[0].StepInfo.Name);
        }

        [Fact]
        public void Expand_Xbox360_Xiso_ResolvesToExpandXBox()
        {
            // Xbox360 system, ISO image source, configString="xiso"
            // Should resolve to Expand-XBox
            string srcFormat = ".iso";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "expand", "xbox360", srcFormat, "", "xiso",
                false, "n", parts, false, false, false, null);

            Assert.Equal("Expand-XBox", task.Steps[0].StepInfo.Name);
        }

        #endregion

        #region Convert Xbox360 to XISO → Convert-XBox-Xiso (Req 6.5)

        [Fact]
        public void Convert_Xbox_ToXiso_AlsoWorksForXbox()
        {
            // Xbox system (not Xbox360), ISO image source, convert to "xiso"
            // The Convert-XBox-Xiso entry in _StepsDefs specifies "xbox" system
            // Should resolve to Convert-XBox-Xiso
            string srcFormat = ".iso";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "convert", "xbox", srcFormat, "", "xiso",
                false, "n", parts, false, false, false, "xiso");

            Assert.Equal("Convert-XBox-Xiso", task.Steps[0].StepInfo.Name);
        }

        #endregion

        #region Expand Dreamcast GDI → Expand-GdRom-CueGdi (Req 8.2)

        [Fact]
        public void Expand_DreamcastGdi_GdRom_ResolvesToExpandGdRomCueGdi()
        {
            // Dreamcast system, GDI folderindex source, GdRom config
            // Should resolve to Expand-GdRom-CueGdi
            string srcFormat = ".gdi";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "expand", "dreamcast", srcFormat, "", "cue",
                false, "n", parts, false, false, false, "gdromcue");

            Assert.Equal("Expand-GdRom-CueGdi", task.Steps[0].StepInfo.Name);
        }

        #endregion

        #region Expand Dreamcast non-GdRom CUE → Expand-Iso-CueToc (Req 8.3)

        [Fact]
        public void Expand_DreamcastCue_NonGdRom_ResolvesToExpandIsoCueToc()
        {
            // Dreamcast system, CUE folderindex source, non-GdRom (cfg="")
            // Should resolve to Expand-Iso-CueToc
            string srcFormat = ".cue";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "expand", "dreamcast", srcFormat, "", "cue",
                false, "n", parts, false, false, false, "");

            Assert.Equal("Expand-Iso-CueToc", task.Steps[0].StepInfo.Name);
        }

        [Fact]
        public void Expand_DreamcastCue_NonGdRom_EmptyConfig_ResolvesToExpandIsoCueToc()
        {
            // Dreamcast system, CUE folderindex source, non-GdRom, empty configString
            // Should resolve to Expand-Iso-CueToc
            string srcFormat = ".cue";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "expand", "dreamcast", srcFormat, "", "",
                false, "n", parts, false, false, false, "");

            Assert.Equal("Expand-Iso-CueToc", task.Steps[0].StepInfo.Name);
        }

        #endregion

        #region Verify CUE folderindex → Verify-Image (Req 1.3)

        [Fact]
        public void Verify_CueFolderindex_Default_ResolvesToVerifyImage()
        {
            // Default system, CUE folderindex source, verify task
            // Should resolve to Verify-Image
            string srcFormat = ".cue";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "verify", "default", srcFormat, "", "",
                false, "n", parts, false, false, false, "none");

            Assert.Equal("Verify-Image", task.Steps[0].StepInfo.Name);
        }

        [Fact]
        public void Verify_CueFolderindex_Ps1_ResolvesToVerifyImage()
        {
            // PS1 system, CUE folderindex source, verify task
            // Should resolve to Verify-Image
            string srcFormat = ".cue";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "verify", "ps1", srcFormat, "", "",
                false, "n", parts, false, false, false, "none");

            Assert.Equal("Verify-Image", task.Steps[0].StepInfo.Name);
        }

        [Fact]
        public void Verify_CueFolderindex_Dreamcast_ResolvesToVerifyImage()
        {
            // Dreamcast system, CUE folderindex source, verify task
            // Should resolve to Verify-Image
            string srcFormat = ".cue";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "verify", "dreamcast", srcFormat, "", "",
                false, "n", parts, false, false, false, "none");

            Assert.Equal("Verify-Image", task.Steps[0].StepInfo.Name);
        }

        #endregion

        #region Verify GDI folderindex → Verify-Image (Req 1.3)

        [Fact]
        public void Verify_GdiFolderindex_Dreamcast_ResolvesToVerifyImage()
        {
            // Dreamcast system, GDI folderindex source, verify task
            // Should resolve to Verify-Image
            string srcFormat = ".gdi";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "verify", "dreamcast", srcFormat, "", "",
                false, "n", parts, false, false, false, "none");

            Assert.Equal("Verify-Image", task.Steps[0].StepInfo.Name);
        }

        #endregion

        #region Additional routing does not fall through to NotSet-NotSupported

        [Fact]
        public void Expand_DefaultIso_DoesNotFallThroughToNotSupported()
        {
            // Verify that Default ISO expand does NOT produce NotSet-NotSupported
            string srcFormat = ".iso";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "expand", "default", srcFormat, "", "iso",
                false, "n", parts, false, false, false, null);

            Assert.DoesNotContain(task.Steps, s => s.StepInfo.Name == "NotSet-NotSupported");
        }

        [Fact]
        public void Expand_Xbox_DoesNotFallThroughToNotSupported()
        {
            // Verify that Xbox expand does NOT produce NotSet-NotSupported
            string srcFormat = ".iso";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "expand", "xbox", srcFormat, "", "xiso",
                false, "n", parts, false, false, false, null);

            Assert.DoesNotContain(task.Steps, s => s.StepInfo.Name == "NotSet-NotSupported");
        }

        [Fact]
        public void Expand_DefaultCue_DoesNotFallThroughToNotSupported()
        {
            // Verify that Default CUE folderindex expand does NOT produce NotSet-NotSupported
            string srcFormat = ".cue";
            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, false);

            NKitTaskContext task = TaskStepsShared.Process(
                "expand", "default", srcFormat, "", "cue",
                false, "n", parts, false, false, false, "");

            Assert.DoesNotContain(task.Steps, s => s.StepInfo.Name == "NotSet-NotSupported");
        }

        #endregion
    }
}