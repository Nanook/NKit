using Nanook.NKit;
using Nanook.NKit.Dats;
using NKDS.DatVerification;

namespace NKDS.DatVerification;

/// <summary>
/// Provides dat file loading, CRC combination, and verification of images against dat entries.
/// </summary>
public sealed class DatVerificationService : IDatVerificationService
{
    /// <inheritdoc />
    public DatLoadResult LoadDat(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new DatLoadResult
            {
                Success = false,
                ErrorMessage = $"File not found: {filePath}"
            };
        }

        try
        {
            using FileStream stream = File.OpenRead(filePath);
            string fileName = Path.GetFileName(filePath);
            Dat dat = Dat.ReadLogixDat(DatCollectionType.Manual, SystemType.NotSet, stream, fileName);

            List<DatEntryInfo> entries = dat.Items.Select(item =>
            {
                uint combinedCrc = ComputeCombinedCrc(item);
                return new DatEntryInfo
                {
                    Name = item.Name,
                    // Dat entry names are plain game titles — they contain dots as part of the name
                    // (e.g. "Super Mario Bros. Wii") and have no file extension. Using
                    // Path.GetFileNameWithoutExtension would incorrectly truncate at the last dot.
                    NameStem = item.Name,
                    CombinedCrc32 = combinedCrc
                };
            }).ToList();

            return new DatLoadResult
            {
                Success = true,
                DatName = dat.Name,
                Entries = entries
            };
        }
        catch (HandledException ex)
        {
            return new DatLoadResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        catch (Exception ex)
        {
            return new DatLoadResult
            {
                Success = false,
                ErrorMessage = $"Failed to parse dat file: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public uint ComputeCombinedCrc(DatItem datItem)
    {
        DatItemPart[] bins = datItem.Bins;
        if (bins == null || bins.Length == 0)
            return 0;

        uint combinedCrc = bins[0].Checksums.Crc;
        for (int i = 1; i < bins.Length; i++)
        {
            combinedCrc = ~NKitDataStore.Crc.Combine(~combinedCrc, ~bins[i].Checksums.Crc, bins[i].Size);
        }

        return combinedCrc;
    }

    /// <inheritdoc />
    public IReadOnlyList<DatResultModel> Verify(
        IReadOnlyList<DatEntryInfo> datEntries,
        IReadOnlyList<ImageInfo> images)
    {
        List<DatResultModel> results = new List<DatResultModel>();

        // Phase 1: Build lookup structures
        // CRC → list of dat entries sharing that CRC
        Dictionary<uint, List<DatEntryInfo>> crcToDatEntries = new Dictionary<uint, List<DatEntryInfo>>();
        foreach (DatEntryInfo entry in datEntries)
        {
            if (!crcToDatEntries.TryGetValue(entry.CombinedCrc32, out List<DatEntryInfo> list))
            {
                list = new List<DatEntryInfo>();
                crcToDatEntries[entry.CombinedCrc32] = list;
            }
            list.Add(entry);
        }

        // Name stem (case-insensitive) → dat entry
        Dictionary<string, DatEntryInfo> nameStemToDatEntry = new Dictionary<string, DatEntryInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (DatEntryInfo entry in datEntries)
        {
            // If multiple dat entries share the same name stem, last one wins
            // (unlikely in practice but handles gracefully)
            nameStemToDatEntry[entry.NameStem] = entry;
        }

        // Track which dat entries are "consumed" (matched by CRC or name)
        HashSet<DatEntryInfo> consumedDatEntries = new HashSet<DatEntryInfo>();

        // Phase 2: Classify images
        // Group images by CRC to handle duplicate CRC tie-breaking
        Dictionary<uint, List<ImageInfo>> imagesByCrc = new Dictionary<uint, List<ImageInfo>>();
        foreach (ImageInfo image in images)
        {
            if (!imagesByCrc.TryGetValue(image.Crc32, out List<ImageInfo> list))
            {
                list = new List<ImageInfo>();
                imagesByCrc[image.Crc32] = list;
            }
            list.Add(image);
        }

        // Process images grouped by CRC for tie-breaking
        HashSet<long> classifiedImages = new HashSet<long>(); // Track classified image IDs

        foreach ((uint crc, List<ImageInfo> imageGroup) in imagesByCrc)
        {
            if (!crcToDatEntries.TryGetValue(crc, out List<DatEntryInfo> matchingDatEntries))
                continue; // No CRC match — will be handled below

            // For each dat entry that matches this CRC, apply tie-breaking
            foreach (DatEntryInfo datEntry in matchingDatEntries)
            {
                consumedDatEntries.Add(datEntry);

                // Find the image whose name stem matches the dat entry (tie-breaking)
                ImageInfo correctImage = null;
                foreach (ImageInfo image in imageGroup)
                {
                    if (classifiedImages.Contains(image.Id))
                        continue;
                    if (string.Equals(image.NameStem, datEntry.NameStem, StringComparison.OrdinalIgnoreCase))
                    {
                        correctImage = image;
                        break;
                    }
                }

                if (correctImage != null)
                {
                    // CRC match + name match → Correct
                    results.Add(new DatResultModel
                    {
                        Status = DatVerificationStatus.Correct,
                        DatEntryName = datEntry.Name,
                        ImageName = correctImage.Name,
                        Crc32 = correctImage.Crc32,
                        ImageId = correctImage.Id,
                        SetName = correctImage.SetName
                    });
                    classifiedImages.Add(correctImage.Id);
                }

                // Remaining unclassified images in this group with this CRC → BadlyNamed
                foreach (ImageInfo image in imageGroup)
                {
                    if (classifiedImages.Contains(image.Id))
                        continue;

                    results.Add(new DatResultModel
                    {
                        Status = DatVerificationStatus.BadlyNamed,
                        DatEntryName = datEntry.Name,
                        ImageName = image.Name,
                        Crc32 = image.Crc32,
                        ImageId = image.Id,
                        SetName = image.SetName
                    });
                    classifiedImages.Add(image.Id);
                }
            }
        }

        // Now classify images that had no CRC match
        foreach (ImageInfo image in images)
        {
            if (classifiedImages.Contains(image.Id))
                continue;

            // No CRC match — check name match
            if (nameStemToDatEntry.TryGetValue(image.NameStem, out DatEntryInfo nameMatchedEntry))
            {
                // No CRC match + name match → WrongCrc
                results.Add(new DatResultModel
                {
                    Status = DatVerificationStatus.WrongCrc,
                    DatEntryName = nameMatchedEntry.Name,
                    ImageName = image.Name,
                    Crc32 = image.Crc32,
                    ImageId = image.Id,
                    SetName = image.SetName
                });
                consumedDatEntries.Add(nameMatchedEntry);
            }
            else
            {
                // No CRC match + no name match → Unmatched
                results.Add(new DatResultModel
                {
                    Status = DatVerificationStatus.Unmatched,
                    DatEntryName = null,
                    ImageName = image.Name,
                    Crc32 = image.Crc32,
                    ImageId = image.Id,
                    SetName = image.SetName
                });
            }
        }

        // Remaining dat entries not consumed → Missing
        foreach (DatEntryInfo entry in datEntries)
        {
            if (!consumedDatEntries.Contains(entry))
            {
                results.Add(new DatResultModel
                {
                    Status = DatVerificationStatus.Missing,
                    DatEntryName = entry.Name,
                    ImageName = null,
                    Crc32 = entry.CombinedCrc32,
                    ImageId = null,
                    SetName = null
                });
            }
        }

        return results;
    }
}