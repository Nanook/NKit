using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nanook.NKit.Container
{
    internal class DataStoreAsIso : Stream, IAsIso
    {
        private Stream _stream;
        private IImageContext _context;
        private long _position;
        private ContainerType _format;
        private byte[] _key;
        private DataStore _dataStore;

        /// <summary>
        /// Rehydrated CHD track metadata for a Dreamcast GD-ROM image stored as
        /// <see cref="ImageFormat.Chd"/>. Populated from the loose chd.meta.txt file during
        /// <see cref="Construct"/> and consumed by the Iso9660 <c>Image</c> reader exactly like
        /// a live <c>ChdAsIso.MetaData</c>, so the deduped source presents as a genuine CHD
        /// (MediaType=GD, tracks carrying Pad/PreGap/ChdMediaType) and export runs the same
        /// GdRomWriter path. Null for all non-CHD formats.
        /// </summary>
        public Nanook.NKit.Chd.ChdMetaData MetaData { get; private set; }

        public ContainerType Format => _format;
        public bool Seekable => _stream.CanSeek;
        public long RealPosition => _stream.Position;
        public long RealSize => _stream.Length;
        public bool SizeEstimated => false;
        public NKitHeader NKitHeader { get; private set; }
        /// <summary>
        /// Optional key discovered while reconstructing the image from the DataStore
        /// (e.g. a title key). This mirrors ImageBuilder.Key for callers that need
        /// to query the DataStore wrapper directly.
        /// </summary>
        public byte[] Key => _key;

        public static IAsIso Create(byte[] id, IImageContext context)
        {
            if (isDataStoreSource(context))
                return new DataStoreAsIso(context);

            return null;
        }

        public DataStoreAsIso(IImageContext context)
        {
            _context = context;
        }

        public int Construct(Stream stream, bool allowSeek)
        {
            string name = _context.SourceFile.Name;
            string knownExt = SourceFiles.GetKnownFileExtension(name);
            if (!string.IsNullOrEmpty(knownExt))
                name = name.Substring(0, name.Length - knownExt.Length);
            string requestedExtension = _context.SourceFile.ImageFiles?.FirstOrDefault()?.Extension;
            string dataStorePath = getDataStorePath();

            System.Diagnostics.Trace.WriteLine($"[DataStoreAsIso] Construct: SourceFile.Name='{_context.SourceFile.Name}' name='{name}' reqExt='{requestedExtension}' dataStorePath='{dataStorePath}'");

            if (string.IsNullOrWhiteSpace(dataStorePath))
                dataStorePath = _context.Settings.DedupePath;

            SystemType systemType = SystemType.NotSet;

            string dataStoreRoot = dataStorePath;
            string setName = systemType.ToString();
            if (!string.IsNullOrWhiteSpace(dataStorePath) && dataStorePath.EndsWith(DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                // Keep the full .nkds path so DataStore will open the exact database file for this set
                string fullPath = Path.GetFullPath(dataStorePath);
                dataStoreRoot = fullPath;
                setName = Path.GetFileNameWithoutExtension(fullPath);
            }

            IImageReader imageReader = null;
            _dataStore = new DataStore(dataStoreRoot);
            List<ImageRecord> images = _dataStore.ListAllImages(img =>
                string.Compare(img.Name, name, true) == 0 &&
                img.SetName == setName &&
                (string.IsNullOrWhiteSpace(requestedExtension) || string.Equals(GetImageExtension(img.Format), requestedExtension, StringComparison.OrdinalIgnoreCase))
            ).ToList();

            if (images.Count == 0)
            {
                images = _dataStore.ListAllImages(img =>
                    string.Compare(img.Name, name, true) == 0 &&
                    img.SetName == setName
                ).ToList();
            }

            // Fallback: try CleanName (strips trailing _N disambiguation suffixes)
            if (images.Count == 0)
            {
                string cleanName = _context.SourceFile.CleanName ?? Regex.Replace(name, @"_[0-9]+$", "");
                if (!string.Equals(cleanName, name, StringComparison.OrdinalIgnoreCase))
                {
                    images = _dataStore.ListAllImages(img =>
                        string.Compare(img.Name, cleanName, true) == 0 &&
                        img.SetName == setName
                    ).ToList();
                }
            }

            if (images.Count == 0)
            {
                _dataStore.Dispose();
                _dataStore = null;
                throw new FileNotFoundException(
                    $"No images found in DataStore set '{setName}' matching name '{name}'. " +
                    $"Ensure images were deduped to the DataStore first.");
            }

            ImageRecord image = images[0];

            // If the scanner extracted a datastore image id from the archive listing,
            // prefer that image directly when it exists in the candidate list.
            try
            {
                if (_context?.SourceFile?.DataStoreImageId != null)
                {
                    long wantId = _context.SourceFile.DataStoreImageId.Value;
                    ImageRecord byId = images.FirstOrDefault(i => i.Id == wantId);
                    if (byId != null)
                        image = byId;
                }
            }
            catch { }

            // If multiple images matched by name, try to disambiguate using a filename
            // contained in the source (e.g., tmd.0 / tmd.1 for WiiU CDN app folders).
            if (images.Count > 1)
            {
                // Build a list of candidate filenames to probe inside the DataStore images.
                // Prefer an explicit OriginalFileName (set by the scanner), then any
                // image part filenames, then the index filename if present.
                List<string> probeNames = new List<string>();
                if (!string.IsNullOrEmpty(_context?.SourceFile?.OriginalFileName))
                    probeNames.Add(_context.SourceFile.OriginalFileName);

                // Add all image part name-only values
                if (_context?.SourceFile?.ImageFiles != null)
                {
                    foreach (SourceFileItem part in _context.SourceFile.ImageFiles)
                    {
                        if (!string.IsNullOrEmpty(part?.NameOnly) && !probeNames.Contains(part.NameOnly, StringComparer.OrdinalIgnoreCase))
                            probeNames.Add(part.NameOnly);
                    }
                }

                // Add index filename if available
                if (!string.IsNullOrEmpty(_context?.SourceFile?.IndexFile?.FileName) && !probeNames.Contains(_context.SourceFile.IndexFile.FileName, StringComparer.OrdinalIgnoreCase))
                    probeNames.Add(_context.SourceFile.IndexFile.FileName);

                // Finally add the canonical SourceFile.Name as a last resort
                if (!string.IsNullOrEmpty(_context?.SourceFile?.Name) && !probeNames.Contains(_context.SourceFile.Name, StringComparer.OrdinalIgnoreCase))
                    probeNames.Add(_context.SourceFile.Name);

                // Probe each candidate filename against each matching image until one succeeds
                bool found = false;
                foreach (string probe in probeNames)
                {
                    if (string.IsNullOrEmpty(probe))
                        continue;

                    foreach (ImageRecord cand in images)
                    {
                        try
                        {
                            byte[] data = _dataStore.ReadFile(new GlobalImageKey(setName, cand.Id), probe);
                            if (data != null)
                            {
                                image = cand;
                                found = true;
                                break;
                            }
                        }
                        catch
                        {
                            // Ignore probe errors and continue
                        }
                    }
                    if (found)
                        break;
                }
            }

            if (Enum.TryParse(image.System, true, out SystemType imageSystemType))
            {
                systemType = imageSystemType;
                _context.SetSystemType(systemType);
            }

            this.Checksums = new Checksums() { Size = image.Size, Crc = image.Crc32, XxHash = image.XxHash64 };
            this.NKitHeader = new NKitHeader(2, this.Checksums.Size != 0, this.Checksums.HasCrc, false, false,
            this.Checksums.Exists(ChecksumType.XxHash), HeaderKeyType.None, 0, false, false, false)
            {
                Size = this.Checksums.Size,
            };
            this.NKitHeader.Checksums.Merge(this.Checksums);

            imageReader = _dataStore.OpenImageReader(new GlobalImageKey(setName, image.Id));

            // Create block provider with aux fallback (auto-discovers aux store by convention)
            string baseDirectory = dataStoreRoot.EndsWith(DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(Path.GetFullPath(dataStoreRoot))!
                : dataStoreRoot;
            IBlockProvider blockProvider = DataStore.CreateBlockProvider(imageReader, baseDirectory, setName);

            if (systemType == SystemType.GameCube)
                _stream = new ImageBuilderGameCubeStream(imageReader, blockProvider: blockProvider);
            else if (systemType == SystemType.Wii)
                _stream = new ImageBuilderWiiStream(imageReader, blockProvider: blockProvider);
            else if (systemType == SystemType.WiiU)
                _stream = new ImageBuilderWiiUStream(imageReader, blockProvider: blockProvider);
            else if (systemType == SystemType.XBox || systemType == SystemType.XBox360)
            {
                // Resolve aux block provider: if the block provider is an AuxBlockProvider,
                // the aux set was discovered by convention ({systemtype}.aux.nkds).
                // Pass it as the auxBlockProvider so the Xbox stream can restore filler data.
                IBlockProvider auxBlockProvider = blockProvider is AuxBlockProvider ? blockProvider : null;
                _stream = new ImageBuilderXboxStream(imageReader, blockProvider: blockProvider, auxBlockProvider: auxBlockProvider);
            }
            else if (isIso9660System(systemType))
            {
                _stream = new ImageBuilderIso9660Stream(imageReader, blockProvider: blockProvider);
            }
            else
            {
                try { imageReader?.Dispose(); } catch { }
                throw new Exception($"System type {systemType} not supported for DataStore reconstruction. Supported types: GameCube, Wii, WiiU, Xbox, Xbox360, and ISO9660-based systems (PS1, PS2, PS3, PSP, SegaCD, Saturn, Dreamcast, CDi, Default, PcEngine).");
            }

            if (_stream is ImageBuilder ib)
            {
                // Only compute an xxhash while rebuilding the image when the DataStore
                // image record actually contains an XxHash value AND the image format
                // is a multi-part format (e.g., .app/.cdn (TMD apps) or .gdi/cue style images).
                // Single-file images used to verify correctly without a streamed XXHash,
                // so avoid unnecessary hashing for those.
                bool isMultiPartFormat = image.Format == NKitDataStore.ImageFormat.App
                    || image.Format == NKitDataStore.ImageFormat.Cdn
                    || image.Format == NKitDataStore.ImageFormat.Gdi
                    || image.Format == NKitDataStore.ImageFormat.Cue
                    || image.Format == NKitDataStore.ImageFormat.Chd;

                if (image.XxHash64 != 0 && isMultiPartFormat)
                {
                    _xxh = XXHash64.Create();
                    ib.OnDataRead = (buf, off, len) => _xxh.TransformBlock(buf, off, len, null, 0);
                }

                // If the builder discovered a persisted key in the DataStore, propagate it
                // to the SourceFile and its items so downstream Image classes can pick it up.
                if (ib.Key != null)
                {
                    _key = ib.Key;
                    _context.SourceFile.Key = ib.Key;
                }
            }

            _format = image.Format switch
            {
                NKitDataStore.ImageFormat.App or NKitDataStore.ImageFormat.Cdn => ContainerType.TmdApp,
                NKitDataStore.ImageFormat.Gdi => ContainerType.Gdi,
                NKitDataStore.ImageFormat.Cue => ContainerType.Cue,
                NKitDataStore.ImageFormat.Chd => ContainerType.Chd,
                _ => ContainerType.Iso
            };

            // Dreamcast GD-ROM stored as ImageFormat.Chd: rehydrate the CHD track metadata from the
            // loose chd.meta.txt file so this source presents identically to a live CHD file. Unlike
            // the CUE/GDI folder-index path we deliberately do NOT set SourceFile.IndexFile — a live
            // CHD has no external index (its layout lives in ChdMetaData), and leaving IndexFile null
            // lets SourceFile.Initialised() keep ImageType=Chd, which is what routes export through
            // the GdRomWriter (ChdGdRomcue) path. The Iso9660 Image reader consumes MetaData exactly
            // as it does ChdAsIso.MetaData.
            if (image.Format == NKitDataStore.ImageFormat.Chd)
            {
                try
                {
                    byte[] metaBytes = imageReader.ReadFile(Steps.Shared.DataStoreIso9660Formatter.ChdMetaFileName);
                    if (metaBytes != null && metaBytes.Length > 0)
                    {
                        string text = System.Text.Encoding.ASCII.GetString(metaBytes);
                        string[] rawLines = text.Replace("\r\n", "\n").Split('\n');
                        int chdBlockSize = 0;
                        List<string> trackLines = new List<string>();
                        foreach (string ln in rawLines)
                        {
                            string line = ln.Trim();
                            if (line.Length == 0)
                                continue;
                            if (line.StartsWith("CHDBLOCKSIZE:", StringComparison.OrdinalIgnoreCase))
                                int.TryParse(line.Substring("CHDBLOCKSIZE:".Length).Trim(), out chdBlockSize);
                            else
                                trackLines.Add(line);
                        }

                        if (trackLines.Count > 0 && chdBlockSize > 0)
                        {
                            // imageSize only affects DVD tracks in ChdMetaData; GD-ROM geometry is
                            // driven by the per-track FRAMES/PAD lines + chdBlockSize.
                            this.MetaData = Nanook.NKit.Chd.ChdMetaData.Parse(image.Name, trackLines, chdBlockSize, image.Size);
                            _context.Log?.Info(() => $"Rehydrated CHD metadata from '{Steps.Shared.DataStoreIso9660Formatter.ChdMetaFileName}' ({trackLines.Count} tracks, blockSize {chdBlockSize}, media {this.MetaData?.MediaType}).");
                        }
                        else
                            _context.Log?.Error(() => $"CHD meta file present but unusable (tracks {trackLines.Count}, blockSize {chdBlockSize}) — deduped Dreamcast CHD export may be incorrect.");
                    }
                    else
                        _context.Log?.Error(() => $"ImageFormat.Chd image '{image.Name}' has no {Steps.Shared.DataStoreIso9660Formatter.ChdMetaFileName} — export cannot reconstruct CHD geometry.");
                }
                catch (Exception ex)
                {
                    _context.Log?.Error(() => $"Failed to rehydrate CHD metadata: {ex.Message}");
                }
            }

            // Attempt to restore original SourceFile structure from DataStore metadata
            // if this image was originally a folder-based image (like WiiU TMD/APP).
            List<AreaRecord> areas = imageReader.GetAreas().ToList();
            List<FileRecord> storedFiles = imageReader.ListFiles().ToList();
            List<AreaRecord> fileAreas = areas.Where(a => a.Metadata?.ContainsKey(AreaValueType.FileName) == true || a.Metadata?.ContainsKey(AreaValueType.App) == true).OrderBy(a => a.Offset).ToList();

            if (fileAreas.Any() || storedFiles.Any())
            {
                // Create a unified list of folder items from areas and the files table
                List<FileItem> folderItems = new();
                Dictionary<string, (long Offset, long Size, long Crc)> fileMeta = new(StringComparer.OrdinalIgnoreCase);

                foreach (AreaRecord fa in fileAreas)
                {
                    string fileName = fa.Metadata[AreaValueType.FileName] ?? fa.Metadata[AreaValueType.App]!;
                    fileMeta[fileName] = (fa.Offset, fa.Size, (long)fa.Crc32);
                    FileItem fi = new FileItem(fileName) { Size = fa.Size, Crc = (long)fa.Crc32 };
                    fi.Populate();

                    // Populate metadata file content for downstream parsers (SiData etc)
                    if (fi.Type == FileItemType.Index || fileName.Contains("tik", StringComparison.OrdinalIgnoreCase) || fileName.Contains("cert", StringComparison.OrdinalIgnoreCase) || fileName.Contains("cetk", StringComparison.OrdinalIgnoreCase) || fileName.Contains("h3", StringComparison.OrdinalIgnoreCase))
                    {
                        // Try loose file storage first (more reliable for metadata)
                        fi.Data = imageReader.ReadFile(fileName);

                        // Fallback to reading from the reconstructed stream if not in the loose file table
                        if (fi.Data == null)
                        {
                            try
                            {
                                long oldPos = _stream.Position;
                                _stream.Position = fa.Offset;
                                fi.Data = new byte[fa.Size];
                                int read = _stream.Read(fi.Data, 0, (int)fa.Size);
                                _stream.Position = oldPos;
                                if (read != (int)fa.Size) fi.Data = null;
                            }
                            catch { }
                        }
                    }
                    folderItems.Add(fi);
                }

                foreach (FileRecord sf in storedFiles)
                {
                    if (!fileMeta.ContainsKey(sf.Name))
                    {
                        fileMeta[sf.Name] = (-1, sf.UncompressedSize, 0); // -1 offset = file table only
                        FileItem fi = new FileItem(sf.Name) { Size = sf.UncompressedSize, Crc = 0 };
                        fi.IsFromDataStore = true;
                        fi.Populate();

                        // Populate loose file content
                        if (fi.Type == FileItemType.Index || sf.Name.Contains("tik", StringComparison.OrdinalIgnoreCase) || sf.Name.Contains("cert", StringComparison.OrdinalIgnoreCase) || sf.Name.Contains("cetk", StringComparison.OrdinalIgnoreCase) || sf.Name.Contains("h3", StringComparison.OrdinalIgnoreCase))
                            fi.Data = imageReader.ReadFile(sf.Name);

                        folderItems.Add(fi);
                    }
                }

                List<SourceFileItem> imageFiles = new();
                IndexFile indexFile = null;

                // 1. First Pass: Try to find and parse the TMD (index file)
                foreach (FileItem fi in folderItems)
                {
                    if ((fi.Type == FileItemType.Index || fi.FileName.ToLower().StartsWith("tmd") || fi.FileName.ToLower().EndsWith(".tmd")) && fi.Data != null)
                    {
                        try
                        {
                            indexFile = IndexFile.Parse(fi.Path, fi.FileName, fi.Extension, fi.Postfix, fi.Data, false, false, folderItems.ToArray());
                            if (indexFile != null) break;
                        }
                        catch { }
                    }
                }

                // 2. Second Pass: Build image file entries for all detected folder files
                foreach (FileItem fi in folderItems)
                {
                    if (indexFile == null || fi.FileName != indexFile.FileName)
                    {
                        if (!fileMeta.TryGetValue(fi.FileName, out (long Offset, long Size, long Crc) meta))
                        {
                            if (string.Equals(fi.FileName, NKitDataStore.DataStore.FileSystemYamlName, StringComparison.OrdinalIgnoreCase))
                            {
                                if (!fileMeta.TryGetValue(NKitDataStore.DataStore.FileSystemYamlRootPath, out meta))
                                    if (!fileMeta.TryGetValue(NKitDataStore.DataStore.FileSystemNkfsRootPath, out meta))
                                        continue;
                            }
                            else if (!fileMeta.TryGetValue(fi.PathFileName, out meta))
                            {
                                continue;
                            }
                        }

                        if (meta.Offset != -1) // only add if it's in the physical image stream, loose files are handled via IndexFile
                            imageFiles.Add(new SourceFileItem("", fi.FileName, fi.Extension, fi.Postfix, meta.Offset, fi.Size, meta.Crc, false, false));
                    }
                }

                if (imageFiles.Count > 0 || indexFile != null)
                {
                    if (_format == ContainerType.Chd)
                    {
                        // Present like a live CHD: a single .chd image entry, NO IndexFile. The Iso9660
                        // reader drives areas/tracks from the rehydrated MetaData (this.MetaData), and
                        // SourceFile.Initialised() maps the .chd extension to SourceImageType.Chd so
                        // export routes through the GdRomWriter (ChdGdRomcue) path.
                        SourceFileItem single = new SourceFileItem("", image.Name + ".chd", ".chd", "", -1, image.Size, (long)image.Crc32, false, false);
                        _context.SourceFile.ImageFiles = new[] { single };
                        _context.SourceFile.Initialised();
                    }
                    else if (_format == ContainerType.Cue || _format == ContainerType.Gdi)
                    {
                        // For folder-based CUE/GDI, present individual track files as ImageFiles
                        // so the scanner can match them against the IndexFile tracks.
                        if (fileAreas.Any(a => a.Metadata?.ContainsKey(AreaValueType.FileName) == true))
                        {
                            _context.SourceFile.ImageFiles = imageFiles.ToArray();
                        }
                        else
                        {
                            // Legacy flat storage fallback
                            string imgExt = _format == ContainerType.Cue ? ".bin" : ".raw";
                            SourceFileItem single = new SourceFileItem("", image.Name + imgExt, imgExt, "", -1, image.Size, (long)image.Crc32, false, false);
                            _context.SourceFile.ImageFiles = new[] { single };
                        }
                    }
                    else if (_format == ContainerType.Iso)
                    {
                        string imgExt = GetImageExtension(image.Format);
                        SourceFileItem single = new SourceFileItem("", image.Name + imgExt, imgExt, "", -1, image.Size, (long)image.Crc32, false, false);
                        _context.SourceFile.ImageFiles = new[] { single };
                    }
                    else
                    {
                        _context.SourceFile.ImageFiles = imageFiles.ToArray();
                    }

                    // ImageFormat.Chd already presented itself as a live-CHD shape (single .chd
                    // ImageFiles entry, no IndexFile, Initialised() called) above — skip the
                    // IndexFile/Initialised handling that would otherwise force ImageType to Cue/Gdi.
                    if (_format != ContainerType.Chd)
                    {
                        // Set IndexFile for all formats that have one (CUE, GDI, TMD).
                        // Previously this was gated on `image.Format != Iso` which incorrectly
                        // excluded CUE/BIN images (stored as ImageFormat.Iso but with a CUE index).
                        // However, WiiU disc images (Format=Iso) should NOT get a TmdApp IndexFile
                        // because they are processed via the disc section path in ConvertWiiUAppTmdStep,
                        // not the folder-based TmdApp path. Setting a TmdApp IndexFile on a disc image
                        // would cause Initialised() to set ImageType=TmdApp, misrouting the Process logic.
                        if (indexFile != null)
                        {
                            bool isTmdAppIndexOnDiscImage = indexFile.FileType == IndexFileType.TmdApp
                                && (image.Format == NKitDataStore.ImageFormat.Iso || image.Format == NKitDataStore.ImageFormat.Unknown);
                            if (!isTmdAppIndexOnDiscImage)
                                _context.SourceFile.IndexFile = indexFile;
                        }

                        // If we reconstructed the index from DataStore contents, ensure
                        // the Additional list only contains items that originated from
                        // the DataStore file table. This prevents local/stream-derived
                        // metadata from being treated as additional parts.
                        if (indexFile?.Additional != null)
                            indexFile.Additional.RemoveAll(a => !a.IsFromDataStore);
                        _context.SourceFile.Initialised();

                        if (_context.SourceFile.ImageType == SourceImageType.TmdApp && (image.Format == NKitDataStore.ImageFormat.App || image.Format == NKitDataStore.ImageFormat.Cdn))
                            _format = ContainerType.TmdApp;

                        // Update container type based on the discovered index file type
                        if (indexFile != null && _format == ContainerType.Iso)
                        {
                            if (indexFile.FileType == IndexFileType.Cue)
                                _format = ContainerType.Cue;
                            else if (indexFile.FileType == IndexFileType.Gdi)
                                _format = ContainerType.Gdi;
                        }
                    }
                }
            }

            // Ensure the SourceFile name reflects the DataStore image name, not the .nkds database name.
            // For App images, strip any [tmd.X] disambiguation suffix added during storage.
            string displayName = image.Format == NKitDataStore.ImageFormat.App
                ? Steps.Shared.DataStoreWiiUFormatter.RestoreBaseNameStatic(image.Name)
                : image.Name;
            _context.SourceFile.Name = displayName;
            _context.SourceFile.CleanName = Regex.Replace(displayName, @"_[0-9]+$", "");

            _position = 0;
            return 0; //keep the default size
        }

        // Add a helper to support IPartsGlobalHash if we can wrap the parts
        private ulong _xxHash;
        private XXHash64 _xxh;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position != _stream.Position)
                _stream.Position = _position;

            int bytesRead = _stream.Read(buffer, offset, count);
            _position += bytesRead;

            // If we reached the end of the logical image, finalize the hash
            if (_position >= Length && _xxHash == 0 && _xxh != null)
            {
                _xxh.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                _xxHash = _xxh.HashUInt64;
                _xxh.Dispose();
                _xxh = null;
            }

            return bytesRead;
        }

        public long Size => _stream.Length;
        public bool SeekRequired => false;

        public Checksums Checksums { get; private set; }

        public Checksums CustomChecksums() => null; //this.Checksums;

        public void Complete()
        {
            try { _stream?.Dispose(); } catch { }
        }

        public void SetRemovedBlock(Action<MetaData> setBlock)
        {
        }

        public override void Flush() => _stream.Flush();

        public override long Position { get => _position; set => _position = value; }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Current)
                _position += offset;
            else if (origin == SeekOrigin.End)
                throw new NotSupportedException();
            else
                _position = offset;
            return _position;
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override bool CanRead => _stream.CanRead;

        public override bool CanSeek => _stream.CanSeek;

        public override bool CanWrite => _stream.CanWrite;

        public override long Length => this.Size;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_xxh != null)
                {
                    _xxh.Dispose();
                    _xxh = null;
                }
            }
            _stream.Dispose();
            try { _dataStore?.Dispose(); } catch { }
            _dataStore = null;
            base.Dispose(disposing);
        }

        private static bool isDataStoreSource(IImageContext context) =>
            string.Equals(context?.SourceFile?.ArchiveFiles?.FirstOrDefault()?.Extension, DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase);

        private string getDataStorePath()
        {
            if (isDataStoreSource(_context))
            {
                SourceFileItem archive = _context.SourceFile.ArchiveFiles[0];
                return Path.Combine(archive.Path, archive.FileName);
            }

            return _context.Settings.DedupePath;
        }

        internal static string GetImageFileName(string imageName, ImageFormat format) => imageName + GetImageExtension(format);

        // ── Duplicate-name disambiguation ────────────────────────────────────────────────────
        // When two images share the same Name in a set, the DataStore listing must present unique
        // entry names. We append the image id in an UNAMBIGUOUS marker: "Name {id}". Braces are
        // never used by redump/no-intro dump names (unlike a bare " (123)", which collides with
        // legitimate year/rev suffixes such as "... (2003)"), so a "{id}" suffix can always be
        // parsed back with zero false positives — no datastore lookup needed to validate it. This
        // is the single source of truth for the format; all write and parse sites route through here.
        private static readonly System.Text.RegularExpressions.Regex _DuplicateNameSuffix =
            new System.Text.RegularExpressions.Regex(@"^(.*) \{(\d+)\}$", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>Appends the disambiguation id to a duplicate image name: <c>"Name {id}"</c>.</summary>
        internal static string FormatDuplicateName(string imageName, long imageId) => $"{imageName} {{{imageId}}}";

        /// <summary>
        /// Cross-set duplicate variant: appends a filesystem-friendly set-name + id marker,
        /// <c>"Name {set_id}"</c>, used when the SAME image name appears in DIFFERENT sets (mount
        /// listings) so both the set and the id disambiguate. The set name is sanitized to
        /// <c>[A-Za-z0-9_-]</c> and capped so it is safe as a filename/entry token.
        /// </summary>
        internal static string FormatDuplicateName(string imageName, string setName, long imageId)
        {
            string safe = System.Text.RegularExpressions.Regex.Replace(setName ?? string.Empty, "[^A-Za-z0-9_-]", "_").Trim('_');
            if (safe.Length > 20)
                safe = safe.Substring(0, 20);
            return $"{imageName} {{{safe}_{imageId}}}";
        }

        /// <summary>
        /// Parses a listing entry name of the form <c>"Name {id}"</c>. Returns true and outputs the
        /// base name + id when the unambiguous <c>{id}</c> marker is present; false otherwise (the
        /// name is used as-is). Unambiguous by construction — no datastore validation required.
        /// </summary>
        internal static bool TryParseDuplicateName(string entryName, out string baseName, out long imageId)
        {
            baseName = entryName;
            imageId = 0;
            if (string.IsNullOrEmpty(entryName))
                return false;
            System.Text.RegularExpressions.Match m = _DuplicateNameSuffix.Match(entryName);
            if (m.Success && long.TryParse(m.Groups[2].Value, out imageId))
            {
                baseName = m.Groups[1].Value;
                return true;
            }
            return false;
        }

        internal static string GetImageExtension(ImageFormat format)
        {
            switch (format)
            {
                case ImageFormat.Bin:
                    return ".bin";
                case ImageFormat.Cue:
                    return ".cue";
                case ImageFormat.App:
                    return ".app";
                case ImageFormat.Gdi:
                    return ".gdi";
                case ImageFormat.CueFolder:
                case ImageFormat.TmdAppFolder:
                    return "";
                case ImageFormat.Cdn:
                case ImageFormat.Iso:
                case ImageFormat.Unknown:
                default:
                    return ".iso";
            }
        }

        private static bool isIso9660System(SystemType systemType) => systemType switch
        {
            SystemType.PS1 => true,
            SystemType.PS2 => true,
            SystemType.PS3 => true,
            SystemType.PSP => true,
            SystemType.SegaCD => true,
            SystemType.Saturn => true,
            SystemType.Dreamcast => true,
            SystemType.CDi => true,
            SystemType.Default => true,
            SystemType.PcEngine => true,
            _ => false
        };
    }
}