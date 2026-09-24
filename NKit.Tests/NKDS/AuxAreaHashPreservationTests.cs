using System;
using System.IO;
using System.Threading;
using NKit.Tests;
using System.Collections.Generic;
using System.Linq;
using Xunit;
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
/// Preservation property tests for aux area hash corruption bugfix.
/// 
/// These tests capture behavior that MUST remain unchanged after the fix is applied.
/// All tests PASS on unfixed code, confirming the baseline behavior to preserve.
/// 
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**
/// </summary>
public class AuxAreaHashPreservationTests : IDisposable
{
    private readonly string _testDirectory;

    public AuxAreaHashPreservationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "NKit_AuxPreservationTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose()
    {
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
    /// Same helper as the bug condition test for consistency.
    /// </summary>
    private static void CreateWiiImageWithUpdatePartition(Stream stream, string discId, int seed)
    {
        byte[] wiiHdr = (byte[])TestImageBuilder.WiiHdr.Clone();

        byte[] ptnHeader = new byte[0x20000];
        Array.Copy(wiiHdr, ptnHeader, Math.Min(wiiHdr.Length, ptnHeader.Length));

        WiiDiscBuilder disc = new WiiDiscBuilder(stream)
        {
            DiscId6 = discId,
            Title = $"NKit Preservation Test Image {seed}"
        };

        // Add Update partition (triggers aux store routing)
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

        byte[] updateFile = new byte[0x100];
        Array.Fill(updateFile, (byte)(seed + 0x10));
        updatePtn.WriteFile(updateFile, 0, "update.bin", "", 0x8000L, updateFile.Length);

        // Add Game partition
        long gamePtnOffset = 0xF800000L;
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
    private static string CreateWiiImageWithUpdate(string directory, int imageIndex)
    {
        string imagePath = Path.Combine(directory, $"WiiImage{imageIndex:D2}.iso");
        using (FileStream fs = File.Create(imagePath))
        {
            string discId = $"NP{imageIndex:D2}WI";
            CreateWiiImageWithUpdatePartition(fs, discId, imageIndex);
        }
        return imagePath;
    }

    /// <summary>
    /// Helper: Processes a single Wii image through the dedupe pipeline.
    /// </summary>
    private NKitTaskResults ProcessImage(string imagePath, string dataStorePath, bool auxEnabled)
    {
        // Format: setName:shardSize:blockSize:autoCreateAux
        string dedupeParam = auxEnabled ? "wii:50g:0:y" : "wii:50g:0:n";

        SystemPresetSettings presets = new SystemPresetSettings
        {
            System = SystemType.Wii,
            Task = TaskType.Dedupe,
            Out = dataStorePath,
            V = Verify.Y,
            ConsoleLevel = LogLevel.None,
            LogOutLevel = LogLevel.None,
            Results = false,
            Dedupe = dedupeParam,
        };
        presets.In.Add(imagePath);

        AppSettings settings = new AppSettings(presets);
        NKitTaskResults results;

        CancellationTokenSource cancel = new CancellationTokenSource();

        using (Log log = settings.GetLog((ms, lv) => { }))
        {
            var images = SourceFiles.Scan(settings.In, settings.R, settings.Arc, true, log, null)
                .OrderBy(a => a.Name).ToList();
            SourceFile img = images.FirstOrDefault();
            if (img == null)
                throw new InvalidOperationException($"No source image found at {imagePath}");

            NKitProcessor p = new NKitProcessor(settings, img, (ms, lv) => { });
            results = p.Process(cancel.Token);
        }

        return results;
    }

    /// <summary>
    /// Property 2a: Non-Aux Mode Section Hash Correctness
    /// 
    /// For all Wii images added WITHOUT aux mode, section-level CRC32 matches
    /// computed CRC over encrypted section data. Verification passes for all images.
    /// 
    /// This confirms that non-aux mode hashing is correct and must remain so after the fix.
    /// 
    /// **Validates: Requirements 3.1, 3.5**
    /// </summary>
    [Property(MaxTest = 1)]
    public Property NonAuxModeSectionHashesAreCorrect()
    {
        string srcDir = Path.Combine(_testDirectory, "src_noaux");
        Directory.CreateDirectory(srcDir);

        string dsDir = Path.Combine(_testDirectory, "ds_noaux");
        Directory.CreateDirectory(dsDir);

        // Create 4 Wii images with Update partitions
        string[] imagePaths = new string[4];
        for (int i = 0; i < 4; i++)
            imagePaths[i] = CreateWiiImageWithUpdate(srcDir, i + 1);

        // Process all 4 images WITHOUT aux mode
        var verificationFailures = new List<string>();

        for (int i = 0; i < 4; i++)
        {
            try
            {
                NKitTaskResults results = ProcessImage(imagePaths[i], dsDir, auxEnabled: false);

                if (results.VerifyResult != VerifyResult.VerifySuccess)
                {
                    verificationFailures.Add(
                        $"Image {i + 1}: Verification={results.VerifyResult}" +
                        (string.IsNullOrEmpty(results.ErrorMsg) ? "" : $" Error={results.ErrorMsg}"));
                }
            }
            catch (Exception ex)
            {
                verificationFailures.Add($"Image {i + 1}: Exception - {ex.Message}");
            }
        }

        bool allVerified = verificationFailures.Count == 0;

        string label = allVerified
            ? "All 4 images verified successfully WITHOUT aux mode (preservation confirmed)"
            : $"UNEXPECTED FAILURES in non-aux mode:\n" +
              string.Join("\n", verificationFailures);

        return allVerified.ToProperty().Label(label);
    }

    /// <summary>
    /// Property 2b: First Image With Aux Mode Section Hashes Are Correct
    /// 
    /// The first Wii image added with aux mode enabled stores correct section-level
    /// hashes and passes verification. This is correct even on unfixed code because
    /// the bug only manifests for images at positions > 1.
    /// 
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Property(MaxTest = 1)]
    public Property FirstImageWithAuxModeHashesAreCorrect()
    {
        string srcDir = Path.Combine(_testDirectory, "src_firstaux");
        Directory.CreateDirectory(srcDir);

        string dsDir = Path.Combine(_testDirectory, "ds_firstaux");
        Directory.CreateDirectory(dsDir);

        // Create a single Wii image with Update partition
        string imagePath = CreateWiiImageWithUpdate(srcDir, 1);

        // Process the FIRST image with aux mode enabled
        string failureMsg = "";
        bool verified = false;

        try
        {
            NKitTaskResults results = ProcessImage(imagePath, dsDir, auxEnabled: true);
            verified = results.VerifyResult == VerifyResult.VerifySuccess;
            if (!verified)
            {
                failureMsg = $"Verification={results.VerifyResult}" +
                    (string.IsNullOrEmpty(results.ErrorMsg) ? "" : $" Error={results.ErrorMsg}");
            }
        }
        catch (Exception ex)
        {
            failureMsg = $"Exception - {ex.Message}";
        }

        string label = verified
            ? "First image with aux mode verified successfully (preservation confirmed)"
            : $"UNEXPECTED FAILURE for first image with aux: {failureMsg}";

        return verified.ToProperty().Label(label);
    }

    /// <summary>
    /// Property 2c: Block-Level CRC32 and XxHash64 Are Correct
    /// 
    /// For all blocks written to primary or aux store, the block-level CRC32 and XxHash64
    /// (stored as the BlockKey) match the actual computed values over the block data.
    /// 
    /// This verifies that individual block hashing is correct regardless of aux mode.
    /// The bug only affects section-level (area-level) hashes, not block-level hashes.
    /// 
    /// **Validates: Requirements 3.3**
    /// </summary>
    [Property(MaxTest = 1)]
    public Property BlockLevelHashesAreCorrectInBothStores()
    {
        string srcDir = Path.Combine(_testDirectory, "src_blocks");
        Directory.CreateDirectory(srcDir);

        string dsDir = Path.Combine(_testDirectory, "ds_blocks");
        Directory.CreateDirectory(dsDir);

        // Create 2 Wii images with Update partitions and process with aux enabled
        string[] imagePaths = new string[2];
        for (int i = 0; i < 2; i++)
            imagePaths[i] = CreateWiiImageWithUpdate(srcDir, i + 1);

        for (int i = 0; i < 2; i++)
        {
            try
            {
                ProcessImage(imagePaths[i], dsDir, auxEnabled: true);
            }
            catch { /* continue - we want to check block hashes regardless */ }
        }

        // Read back all blocks from both primary and aux stores and verify their hashes
        var hashMismatches = new List<string>();
        int totalBlocksChecked = 0;

        try
        {
            using (var ds = new DataStore(dsDir))
            {
                // Check primary set blocks
                var primaryImages = ds.ListImagesInSet("wii");
                foreach (var img in primaryImages)
                {
                    using (var reader = ds.OpenImageReader(new GlobalImageKey("wii", img.Id)))
                    {
                        var offsets = reader.GetOffsets().Where(o => o.HasBlocks).ToList();
                        foreach (var offset in offsets)
                        {
                            for (int i = 0; i < offset.BlockCount; i++)
                            {
                                BlockKey key = offset.GetBlockAt(i);
                                BlockRecord block = reader.GetBlock(key);
                                if (block == null)
                                {
                                    hashMismatches.Add(
                                        $"Primary {img.Name}: Block at offset {offset.Offset} index {i} not found");
                                    continue;
                                }

                                // Verify the block key's CRC32 and XxHash64 match the actual data
                                uint computedCrc = Nanook.NKit.Crc.Compute(block.Data);
                                ulong computedXx = Nanook.GrindCore.XXHash.XXHash64.Compute(block.Data);

                                if (key.Crc32 != computedCrc)
                                {
                                    hashMismatches.Add(
                                        $"Primary {img.Name}: Block CRC mismatch at offset {offset.Offset} index {i}: " +
                                        $"stored={key.Crc32:X8}, computed={computedCrc:X8}");
                                }
                                if (key.XxHash64 != computedXx)
                                {
                                    hashMismatches.Add(
                                        $"Primary {img.Name}: Block XxHash mismatch at offset {offset.Offset} index {i}: " +
                                        $"stored={key.XxHash64:X16}, computed={computedXx:X16}");
                                }
                                totalBlocksChecked++;
                            }
                        }
                    }
                }

                // Check aux set blocks
                string auxSetName = DataStore.ResolveAuxSetName(
                    Path.Combine(dsDir, "wii" + DataStore.DatabaseFileExtension));
                if (auxSetName != null)
                {
                    var auxImages = ds.ListImagesInSet(auxSetName);
                    foreach (var img in auxImages)
                    {
                        using (var reader = ds.OpenImageReader(new GlobalImageKey(auxSetName, img.Id)))
                        {
                            var offsets = reader.GetOffsets().Where(o => o.HasBlocks).ToList();
                            foreach (var offset in offsets)
                            {
                                for (int i = 0; i < offset.BlockCount; i++)
                                {
                                    BlockKey key = offset.GetBlockAt(i);
                                    BlockRecord block = reader.GetBlock(key);
                                    if (block == null)
                                    {
                                        hashMismatches.Add(
                                            $"Aux {img.Name}: Block at offset {offset.Offset} index {i} not found");
                                        continue;
                                    }

                                    uint computedCrc = Nanook.NKit.Crc.Compute(block.Data);
                                    ulong computedXx = Nanook.GrindCore.XXHash.XXHash64.Compute(block.Data);

                                    if (key.Crc32 != computedCrc)
                                    {
                                        hashMismatches.Add(
                                            $"Aux {img.Name}: Block CRC mismatch at offset {offset.Offset} index {i}: " +
                                            $"stored={key.Crc32:X8}, computed={computedCrc:X8}");
                                    }
                                    if (key.XxHash64 != computedXx)
                                    {
                                        hashMismatches.Add(
                                            $"Aux {img.Name}: Block XxHash mismatch at offset {offset.Offset} index {i}: " +
                                            $"stored={key.XxHash64:X16}, computed={computedXx:X16}");
                                    }
                                    totalBlocksChecked++;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            hashMismatches.Add($"DataStore read error: {ex.Message}");
        }

        bool allCorrect = hashMismatches.Count == 0 && totalBlocksChecked > 0;

        string label = allCorrect
            ? $"All {totalBlocksChecked} block-level hashes verified correct in both stores (preservation confirmed)"
            : hashMismatches.Count > 0
                ? $"BLOCK HASH MISMATCHES:\n" + string.Join("\n", hashMismatches.Take(10))
                : "No blocks found to verify";

        return allCorrect.ToProperty().Label(label);
    }

    /// <summary>
    /// Property 2d: Update Partition Blocks Route to Aux, Non-Update to Primary
    /// 
    /// When aux mode is enabled, update partition blocks are routed to the aux store
    /// and non-update-partition blocks (game partition) are routed to the primary store.
    /// 
    /// This verifies the routing logic is correct and must remain unchanged after the fix.
    /// 
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 1)]
    public Property UpdatePartitionBlocksRouteToAuxNonUpdateToPrimary()
    {
        string srcDir = Path.Combine(_testDirectory, "src_routing");
        Directory.CreateDirectory(srcDir);

        string dsDir = Path.Combine(_testDirectory, "ds_routing");
        Directory.CreateDirectory(dsDir);

        // Create a single Wii image with Update partition and process with aux enabled
        string imagePath = CreateWiiImageWithUpdate(srcDir, 1);

        try
        {
            ProcessImage(imagePath, dsDir, auxEnabled: true);
        }
        catch (Exception ex)
        {
            return false.ToProperty().Label($"Failed to process image: {ex.Message}");
        }

        // Verify routing: primary store should have blocks, aux store should have blocks
        bool primaryHasBlocks = false;
        bool auxHasBlocks = false;
        int primaryBlockCount = 0;
        int auxBlockCount = 0;
        string routingError = "";

        try
        {
            using (var ds = new DataStore(dsDir))
            {
                // Check primary set has images with blocks (game partition data)
                var primaryImages = ds.ListImagesInSet("wii");
                foreach (var img in primaryImages)
                {
                    using (var reader = ds.OpenImageReader(new GlobalImageKey("wii", img.Id)))
                    {
                        var offsets = reader.GetOffsets().Where(o => o.HasBlocks).ToList();
                        foreach (var offset in offsets)
                        {
                            primaryBlockCount += offset.BlockCount;
                        }
                    }
                }
                primaryHasBlocks = primaryBlockCount > 0;

                // Check aux set has images with blocks (update partition data)
                string auxSetName = DataStore.ResolveAuxSetName(
                    Path.Combine(dsDir, "wii" + DataStore.DatabaseFileExtension));
                if (auxSetName != null)
                {
                    var auxImages = ds.ListImagesInSet(auxSetName);
                    foreach (var img in auxImages)
                    {
                        using (var reader = ds.OpenImageReader(new GlobalImageKey(auxSetName, img.Id)))
                        {
                            var offsets = reader.GetOffsets().Where(o => o.HasBlocks).ToList();
                            foreach (var offset in offsets)
                            {
                                auxBlockCount += offset.BlockCount;
                            }
                        }
                    }
                    auxHasBlocks = auxBlockCount > 0;
                }
                else
                {
                    routingError = "Aux set not found - routing may not be working";
                }
            }
        }
        catch (Exception ex)
        {
            routingError = $"DataStore read error: {ex.Message}";
        }

        bool routingCorrect = primaryHasBlocks && auxHasBlocks && string.IsNullOrEmpty(routingError);

        string label = routingCorrect
            ? $"Routing correct: primary has {primaryBlockCount} blocks (game), aux has {auxBlockCount} blocks (update) (preservation confirmed)"
            : $"ROUTING ISSUE: primaryBlocks={primaryBlockCount}, auxBlocks={auxBlockCount}" +
              (string.IsNullOrEmpty(routingError) ? "" : $" Error: {routingError}");

        return routingCorrect.ToProperty().Label(label);
    }
}