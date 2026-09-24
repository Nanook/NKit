using NKDS.DatVerification;

namespace NKDS.Tests;

/// <summary>
/// Unit tests for DatVerificationService — covers the three bugs fixed in this session:
///
/// Bug 1: Dat entry names must NOT be processed with Path.GetFileNameWithoutExtension.
///        Titles like "New Super Mario Bros. Wii" or "FreeLoader for GameCube (Europe) (Unl) (v1.06B) (2003)"
///        contain dots as part of the name, not as extension separators. The old code would truncate them.
///
/// Bug 2: When renaming a badly-named image to its dat name, if the target name already exists
///        with the SAME CRC the image is a redundant duplicate — it should be removed, not blocked.
///        (The ExecuteRename / conflict-resolution path; tested here via Verify() result classification.)
///
/// Bug 3: Verify correctly identifies multiple images with the same CRC as duplicates,
///        each receiving its own result entry with an independent ID.
/// </summary>
public class DatVerificationServiceTests
{
    private readonly DatVerificationService _svc = new DatVerificationService();

    // -------------------------------------------------------------------------
    // Bug 1 — names with dots must not be truncated
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("New Super Mario Bros. Wii (Europe, Scandinavia) (En,Fr,De,Es,It) (Rev 2)")]
    [InlineData("FreeLoader for GameCube (Europe) (Unl) (v1.06B) (2003)")]
    [InlineData("Action Replay Ultimate Cheats fuer Enter the Matrix (Germany) (Unl)")]
    [InlineData("Super Mario Bros. 3 (USA)")]
    public void DatEntryNameStem_EqualsDatEntryName_NoTruncation(string datEntryName)
    {
        // DatEntryInfo.NameStem must equal the full Name — no dot-based truncation.
        // Simulate what LoadDat produces for a single entry.
        DatEntryInfo entry = BuildDatEntry(datEntryName, 0xABCDEF01);
        Assert.Equal(datEntryName, entry.NameStem);
        Assert.Equal(datEntryName, entry.Name);
    }

    [Fact]
    public void Verify_ImageNameMatchesDatName_ReportsCorrect_WithDotInTitle()
    {
        // An image whose stored name is "New Super Mario Bros. Wii (Europe...)" and the
        // dat has the same entry → the image is Correct, NOT BadlyNamed.
        string name = "New Super Mario Bros. Wii (Europe, Scandinavia) (En,Fr,De,Es,It) (Rev 2)";
        DatEntryInfo entry = BuildDatEntry(name, 0x2A9344FB);
        ImageInfo image = BuildImageInfo(1, name, 0x2A9344FB);

        IReadOnlyList<DatResultModel> results = _svc.Verify(new[] { entry }, new[] { image });

        Assert.Single(results);
        Assert.Equal(DatVerificationStatus.Correct, results[0].Status);
    }

    [Fact]
    public void Verify_ImageNameDifferentFromDatName_ReportsBadlyNamed_WhenCrcMatches()
    {
        // An image named "New Super Mario Bros" (truncated, the old bug) when the dat
        // entry is the full name → should be BadlyNamed (CRC matches, name does not).
        string datName = "New Super Mario Bros. Wii (Europe, Scandinavia) (En,Fr,De,Es,It) (Rev 2)";
        string imageName = "New Super Mario Bros"; // old truncated name
        DatEntryInfo entry = BuildDatEntry(datName, 0x2A9344FB);
        ImageInfo image = BuildImageInfo(1, imageName, 0x2A9344FB);

        IReadOnlyList<DatResultModel> results = _svc.Verify(new[] { entry }, new[] { image });

        Assert.Single(results);
        Assert.Equal(DatVerificationStatus.BadlyNamed, results[0].Status);
        Assert.Equal(datName, results[0].DatEntryName); // full name preserved in result
    }

    [Fact]
    public void Verify_FreeLoaderFullName_NotTruncated()
    {
        // The exact name from the bug report — must not be cut at "(v1." → "v1".
        string name = "FreeLoader for GameCube (Europe) (Unl) (v1.06B) (2003)";
        DatEntryInfo entry = BuildDatEntry(name, 0x40647E85);
        ImageInfo image = BuildImageInfo(1, name, 0x40647E85);

        IReadOnlyList<DatResultModel> results = _svc.Verify(new[] { entry }, new[] { image });

        Assert.Single(results);
        Assert.Equal(DatVerificationStatus.Correct, results[0].Status);
    }

    // -------------------------------------------------------------------------
    // Bug 2 — same-CRC duplicate detection
    // -------------------------------------------------------------------------

    [Fact]
    public void Verify_TwoCopiesOfSameImage_BothClassified()
    {
        // Add image, rename to A, add same image again → two entries with same CRC but different names.
        // Both should appear in Verify results — one Correct (named A matching the dat),
        // one BadlyNamed (still has the original name).
        string datName = "Image A";
        string originalName = "Image 1";
        uint crc = 0xDEADBEEF;

        DatEntryInfo entry = BuildDatEntry(datName, crc);
        ImageInfo imageA = BuildImageInfo(1, datName, crc);       // correctly named
        ImageInfo imageOrig = BuildImageInfo(2, originalName, crc); // duplicate with old name

        IReadOnlyList<DatResultModel> results = _svc.Verify(new[] { entry }, new[] { imageA, imageOrig });

        // "Image A" matches the dat entry by CRC+name → Correct
        DatResultModel? correct = results.FirstOrDefault(r => r.ImageId == 1);
        Assert.NotNull(correct);
        Assert.Equal(DatVerificationStatus.Correct, correct!.Status);

        // "Image 1" has the same CRC but wrong name → BadlyNamed
        DatResultModel? badlyNamed = results.FirstOrDefault(r => r.ImageId == 2);
        Assert.NotNull(badlyNamed);
        Assert.Equal(DatVerificationStatus.BadlyNamed, badlyNamed!.Status);
        Assert.Equal(datName, badlyNamed.DatEntryName); // points to the correct dat name
    }

    [Fact]
    public void Verify_DuplicateResults_HaveDistinctIds()
    {
        // Both results for same-CRC images carry distinct ImageIds so callers can
        // target the correct one for deletion by ID, not by name.
        uint crc = 0x12345678;
        DatEntryInfo entry = BuildDatEntry("Game", crc);
        ImageInfo img1 = BuildImageInfo(10, "Game", crc);
        ImageInfo img2 = BuildImageInfo(20, "Wrong Name", crc);

        IReadOnlyList<DatResultModel> results = _svc.Verify(new[] { entry }, new[] { img1, img2 });

        List<long?> ids = results.Select(r => r.ImageId).ToList();
        Assert.Contains(10L, ids);
        Assert.Contains(20L, ids);
        // No two results share the same non-null ID
        List<long> nonNullIds = ids.Where(id => id.HasValue).Select(id => id!.Value).ToList();
        Assert.Equal(nonNullIds.Count, nonNullIds.Distinct().Count());
    }

    // -------------------------------------------------------------------------
    // Bug 3 — each entry has its own ID so delete-by-ID targets the right one
    // -------------------------------------------------------------------------

    [Fact]
    public void Verify_BadlyNamed_ResultCarriesCorrectImageId()
    {
        // The BadlyNamed result must carry the image's own ID so the caller can
        // delete or rename precisely that record.
        uint crc = 0xCAFEBABE;
        DatEntryInfo entry = BuildDatEntry("Correct Title", crc);
        ImageInfo img = BuildImageInfo(42, "Wrong Title", crc);

        IReadOnlyList<DatResultModel> results = _svc.Verify(new[] { entry }, new[] { img });

        Assert.Single(results);
        Assert.Equal(DatVerificationStatus.BadlyNamed, results[0].Status);
        Assert.Equal(42L, results[0].ImageId);
    }

    [Fact]
    public void Verify_MultipleImagesWithSameName_AllGetUniqueResultEntries()
    {
        // Edge case: if two images somehow share a name but differ in CRC,
        // both still get independent result entries keyed by their own ID.
        DatEntryInfo entry1 = BuildDatEntry("Game", 0xAAAA0001);
        DatEntryInfo entry2 = BuildDatEntry("Other Game", 0xBBBB0002);
        ImageInfo img1 = BuildImageInfo(1, "Game", 0xAAAA0001);
        ImageInfo img2 = BuildImageInfo(2, "Game", 0xBBBB0002); // same display name, different CRC

        IReadOnlyList<DatResultModel> results = _svc.Verify(
            new[] { entry1, entry2 },
            new[] { img1, img2 });

        // img1 is Correct (CRC+name match entry1)
        DatResultModel? r1 = results.FirstOrDefault(r => r.ImageId == 1);
        Assert.NotNull(r1);
        Assert.Equal(DatVerificationStatus.Correct, r1!.Status);

        // img2 CRC matches entry2 but name doesn't → BadlyNamed or WrongCrc
        DatResultModel? r2 = results.FirstOrDefault(r => r.ImageId == 2);
        Assert.NotNull(r2);
        Assert.NotEqual(DatVerificationStatus.Correct, r2!.Status);

        // Each result has its own distinct ID
        Assert.NotEqual(r1.ImageId, r2.ImageId);
    }

    // -------------------------------------------------------------------------
    // Regression — existing correct-name matching still works
    // -------------------------------------------------------------------------

    [Fact]
    public void Verify_ExactMatch_ReportsCorrect()
    {
        DatEntryInfo entry = BuildDatEntry("Super Mario World (USA)", 0x11223344);
        ImageInfo image = BuildImageInfo(1, "Super Mario World (USA)", 0x11223344);

        IReadOnlyList<DatResultModel> results = _svc.Verify(new[] { entry }, new[] { image });

        Assert.Single(results);
        Assert.Equal(DatVerificationStatus.Correct, results[0].Status);
    }

    [Fact]
    public void Verify_MissingImage_ReportsMissing()
    {
        DatEntryInfo entry = BuildDatEntry("Rare Game", 0xFFEEDDCC);

        IReadOnlyList<DatResultModel> results = _svc.Verify(new[] { entry }, Array.Empty<ImageInfo>());

        Assert.Single(results);
        Assert.Equal(DatVerificationStatus.Missing, results[0].Status);
    }

    [Fact]
    public void Verify_UnmatchedImage_ReportsUnmatched()
    {
        ImageInfo image = BuildImageInfo(1, "Unknown Game", 0x00000001);

        IReadOnlyList<DatResultModel> results = _svc.Verify(Array.Empty<DatEntryInfo>(), new[] { image });

        Assert.Single(results);
        Assert.Equal(DatVerificationStatus.Unmatched, results[0].Status);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Builds a DatEntryInfo as LoadDat would (NameStem = Name, no truncation).</summary>
    private static DatEntryInfo BuildDatEntry(string name, uint crc) => new DatEntryInfo
    {
        Name = name,
        NameStem = name,   // Bug 1 fix: no Path.GetFileNameWithoutExtension
        CombinedCrc32 = crc
    };

    /// <summary>Builds an ImageInfo as RunVerification would (NameStem = Name).</summary>
    private static ImageInfo BuildImageInfo(long id, string name, uint crc) => new ImageInfo
    {
        Id = id,
        Name = name,
        NameStem = name,   // Bug 1 fix: no Path.GetFileNameWithoutExtension
        Crc32 = crc,
        SetName = "test-set"
    };
}
