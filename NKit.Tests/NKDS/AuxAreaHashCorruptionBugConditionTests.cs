using System;
using System.IO;
using System.Threading;
using NKit.Tests;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using System.Diagnostics;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using Nanook.NKit.Builder;
using Nanook.NKit.Nintendo.WiiGc;
using NKitDataStore;

namespace NKit.Tests.NKDS;

/// <summary>
/// Bug condition exploration test for aux area hash corruption.
/// 
/// This test verifies that adding 4 Wii images to a DataStore with aux mode enabled
/// produces correct section-level CRC32 for ALL images. The bug manifests as incorrect
/// Area/Section-level CRC32 and XxHash64 values stored in the Image_Metadata_Section
/// for images at even positions (2nd, 4th) in a batch.
/// 
/// **Validates: Requirements 1.1, 1.2, 1.3, 1.4**
/// 
/// EXPECTED: This test FAILS on unfixed code (proving the bug exists).
/// Images at even positions (2nd, 4th) will have incorrect stored section CRC values
/// due to stale hash accumulator state between images when aux mode is active.
/// </summary>
public class AuxAreaHashCorruptionBugConditionTests : IDisposable
{
    private readonly string _testDirectory;

    public AuxAreaHashCorruptionBugConditionTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "NKit_AuxHashBugTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose()
    {
        // Force GC to release file handles before cleanup
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Thread.Sleep(200);

        try
        {
            if (Directory.Exists(_testDirectory))
                Directory.Delete(_testDirectory, true);
        }
        catch { /* ignore cleanup failures */ }
    }

    /// <summary>
    /// Creates a Wii ISO image with both an Update partition and a Game partition.
    /// The Update partition is required for the bug to manifest because it triggers
    /// aux store routing.
    /// </summary>
    private static void CreateWiiImageWithUpdatePartition(Stream stream, string discId, int seed)
    {
        // Decode the Wii public key header (same as TestImageBuilder)
        byte[] wiiHdr = (byte[])TestImageBuilder.WiiHdr.Clone();

        byte[] ptnHeader = new byte[0x20000];
        Array.Copy(wiiHdr, ptnHeader, Math.Min(wiiHdr.Length, ptnHeader.Length));

        WiiDiscBuilder disc = new WiiDiscBuilder(stream)
        {
            DiscId6 = discId,
            Title = $"NKit Bug Test Image {seed}"
        };

        // Add Update partition first (at the default update partition offset)
        // This is critical - the bug only manifests when an update partition exists
        // because that's what triggers aux store routing
        WiiPartitionBuilder updatePtn = disc.AddPartition(
            discId, ptnHeader, WiiConsts.WiiDefaultUpdatePtnOffset, PartitionType.Update);

        byte[] updateHeader = new byte[WiiConsts.BootBinSize];
        updateHeader.WriteString(WiiConsts.DataHdrIdOffset, discId.Length, discId);
        updateHeader.WriteUInt32B(0x1c, 0xC2339F3D);
        updateHeader.WriteString(WiiConsts.DataHdrTitleOffset, $"Update {seed}".Length, $"Update {seed}");

        byte[] updateBi2 = new byte[WiiConsts.AppLoaderOffset - WiiConsts.BootBinSize];
        updateBi2.WriteUInt32B(0x18, (uint)Region.Pal);

        byte[] updateAppldr = new byte[0x2000];
        updateAppldr.WriteString(0, 10, new DateTime(1996, 1, 1).ToString("yyyy/MM/dd"));
        updateAppldr.WriteUInt32B(0x14, 0x800 - 0x20);
        updateAppldr.WriteUInt32B(0x18, 0x1800);

        byte[] updateMainDol = new byte[0x2000];
        updateMainDol.WriteUInt32B(0x0, 0x100);
        updateMainDol.WriteUInt32B(0x1c, 0x1e00);

        updatePtn.SetDataHeader(updateHeader, updateBi2, updateAppldr, updateMainDol, 0);

        // Add a file to the update partition
        byte[] updateFile = new byte[0x100];
        Array.Fill(updateFile, (byte)(seed + 0x10));
        updatePtn.WriteFile(updateFile, 0, "update.bin", "", 0x8000L, updateFile.Length);

        // Add Game partition (at a higher offset)
        long gamePtnOffset = 0xF800000L; // Standard game partition offset
        WiiPartitionBuilder gamePtn = disc.AddPartition(
            discId, ptnHeader, gamePtnOffset, PartitionType.Game);

        byte[] gameHeader = new byte[WiiConsts.BootBinSize];
        gameHeader.WriteString(WiiConsts.DataHdrIdOffset, discId.Length, discId);
        gameHeader.WriteUInt32B(0x1c, 0xC2339F3D);
        gameHeader.WriteString(WiiConsts.DataHdrTitleOffset, $"Game {seed}".Length, $"Game {seed}");

        byte[] gameBi2 = new byte[WiiConsts.AppLoaderOffset - WiiConsts.BootBinSize];
        gameBi2.WriteUInt32B(0x18, (uint)Region.Pal);

        byte[] gameAppldr = new byte[0x2000];
        gameAppldr.WriteString(0, 10, new DateTime(1996, 1, 1).ToString("yyyy/MM/dd"));
        gameAppldr.WriteUInt32B(0x14, 0x800 - 0x20);
        gameAppldr.WriteUInt32B(0x18, 0x1800);

        byte[] gameMainDol = new byte[0x2000];
        gameMainDol.WriteUInt32B(0x0, 0x100);
        gameMainDol.WriteUInt32B(0x1c, 0x1e00);

        gamePtn.SetDataHeader(gameHeader, gameBi2, gameAppldr, gameMainDol, 0);

        // Add files to the game partition
        byte[] gameFile = new byte[0x100];
        Array.Fill(gameFile, (byte)(seed + 1));
        gamePtn.WriteFile(gameFile, 0, "main.dol", "", 0x8000L, gameFile.Length);

        byte[] gameFile2 = new byte[0x100];
        Array.Fill(gameFile2, (byte)(seed + 2));
        gamePtn.WriteFile(gameFile2, 0, "data.bin", "files", 4, gameFile2.Length);

        disc.Complete();
    }

    /// <summary>
    /// Helper: Creates a Wii ISO image file with an Update partition at the given path.
    /// </summary>
    private static string CreateWiiImageWithUpdate(string directory, int imageIndex, int seed = -1)
    {
        string imagePath = Path.Combine(directory, $"WiiImage{imageIndex:D2}.iso");
        using (FileStream fs = File.Create(imagePath))
        {
            // Use a unique disc ID per image to avoid deduplication
            string discId = $"NK{imageIndex:D2}WI";
            CreateWiiImageWithUpdatePartition(fs, discId, seed < 0 ? imageIndex : seed);
        }
        return imagePath;
    }

    /// <summary>
    /// Helper: Processes a single Wii image through the dedupe pipeline with aux mode enabled.
    /// Returns the NKitTaskResults for inspection.
    /// </summary>
    private NKitTaskResults ProcessImageWithAux(string imagePath, string dataStorePath)
    {
        // Configure dedupe with aux mode enabled (4th part = "y")
        // Format: setName:shardSize:blockSize:autoCreateAux
        string dedupeParam = "wii:50g:0:y";

        SystemPresetSettings presets = new SystemPresetSettings
        {
            System = SystemType.Wii,
            Task = TaskType.Dedupe,
            Out = dataStorePath,
            V = Verify.Y, // Enable verification to detect the bug
            ConsoleLevel = LogLevel.None,
            LogOutLevel = LogLevel.None,
            Results = false,
            Dedupe = dedupeParam,
        };
        presets.In.Add(imagePath);

        AppSettings settings = new AppSettings(presets);
        NKitTaskResults results;

        CancellationTokenSource cancel = new CancellationTokenSource();

        using (Log log = settings.GetLog((ms, lv) => { /* suppress output */ }))
        {
            var images = SourceFiles.Scan(settings.In, settings.R, settings.Arc, true, log, null)
                .OrderBy(a => a.Name).ToList();
            SourceFile img = images.FirstOrDefault();
            if (img == null)
                throw new InvalidOperationException($"No source image found at {imagePath}");

            NKitProcessor p = new NKitProcessor(settings, img, (ms, lv) => { /* suppress */ });
            results = p.Process(cancel.Token);
        }

        return results;
    }

    /// <summary>
    /// Property 1: Bug Condition - Aux Mode Section Hash Corruption on Even-Position Images
    /// 
    /// Adds 4 Wii images (each with an Update partition) to a DataStore with aux mode
    /// enabled and verifies that ALL images pass verification. The verification step
    /// compares stored section-level CRC32/XxHash64 against reconstructed section data.
    /// 
    /// On unfixed code, this test FAILS for images at even positions (2nd, 4th) because
    /// the stored section CRC is corrupted due to stale hash accumulator state.
    /// 
    /// **Validates: Requirements 1.1, 1.2, 1.3, 1.4**
    /// </summary>
    [Property(MaxTest = 1)]
    public Property AuxModeSectionHashCorrectnessForAllBatchPositions()
    {
        // Create source images directory
        string srcDir = Path.Combine(_testDirectory, "src");
        Directory.CreateDirectory(srcDir);

        // Create DataStore directory
        string dsDir = Path.Combine(_testDirectory, "ds");
        Directory.CreateDirectory(dsDir);

        // Create 4 Wii images with Update partitions
        string[] imagePaths = new string[4];
        for (int i = 0; i < 4; i++)
            imagePaths[i] = CreateWiiImageWithUpdate(srcDir, i + 1);

        // Process all 4 images through dedupe with aux mode enabled
        var verificationFailures = new List<string>();
        var imageResults = new List<(int Position, VerifyResult Result, string Error)>();

        for (int i = 0; i < 4; i++)
        {
            try
            {
                NKitTaskResults results = ProcessImageWithAux(imagePaths[i], dsDir);
                imageResults.Add((i + 1, results.VerifyResult, results.ErrorMsg ?? ""));

                // Check if verification failed (this is the bug symptom)
                if (results.VerifyResult != VerifyResult.VerifySuccess)
                {
                    verificationFailures.Add(
                        $"Image {i + 1}: Verification={results.VerifyResult}" +
                        (string.IsNullOrEmpty(results.ErrorMsg) ? "" : $" Error={results.ErrorMsg}"));
                }
            }
            catch (Exception ex)
            {
                imageResults.Add((i + 1, VerifyResult.Error, ex.Message));
                verificationFailures.Add($"Image {i + 1}: Exception - {ex.Message}");
            }
        }

        // Also read back stored area CRC values for diagnostic output
        var storedCrcs = new List<string>();
        try
        {
            using (var ds = new DataStore(dsDir))
            {
                var storedImages = ds.ListAllImages(img => true).ToList();
                foreach (var img in storedImages)
                {
                    using (var reader = ds.OpenImageReader(new GlobalImageKey(img.SetName, img.Id)))
                    {
                        var areas = reader.GetAreas().ToList();
                        foreach (var area in areas)
                        {
                            storedCrcs.Add(
                                $"  {img.Name} (set={img.SetName}): offset={area.Offset:X}, " +
                                $"CRC32={area.Crc32:X8}, XxHash64={area.XxHash64:X16}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            storedCrcs.Add($"  DataStore read error: {ex.Message}");
        }

        // The property: ALL images must verify successfully
        // On unfixed code, images 2 and 4 will fail verification
        bool allVerified = verificationFailures.Count == 0;

        string label = allVerified
            ? "All 4 images verified successfully with aux mode enabled"
            : $"VERIFICATION FAILURES (bug confirmed - even-position images corrupted):\n" +
              string.Join("\n", verificationFailures) +
              $"\n\nAll image results:\n" +
              string.Join("\n", imageResults.Select(r =>
                  $"  Image {r.Position}: {r.Result}" +
                  (string.IsNullOrEmpty(r.Error) ? "" : $" ({r.Error})"))) +
              $"\n\nStored area CRC values:\n" +
              string.Join("\n", storedCrcs);

        return allVerified.ToProperty().Label(label);
    }
}
