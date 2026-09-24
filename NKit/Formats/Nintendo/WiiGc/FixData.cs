using Nanook.NKit.Settings;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nanook.NKit.Nintendo.WiiGc
{

    internal class FixData : IFixData
    {
        private Dictionary<object, object> _yaml;
        private string _filesPath;
        private List<FixPartition> _wiiUpdatePartitions;
        private List<FixPartition> _wiiChannels;
        private List<FixFileItem> _gcBinFiles;

        public List<FixFileItem> GcBinFiles
        {
            get
            {
                if (this.SystemType == SystemType.GameCube && _gcBinFiles == null)
                    populateGcBins();
                return _gcBinFiles;
            }
        }

        public List<FixPartition> WiiUpdatePartitions
        {
            get
            {
                if (this.SystemType == SystemType.Wii && _wiiUpdatePartitions == null)
                    populatePartitions();
                return _wiiUpdatePartitions;
            }
        }
        public List<FixPartition> WiiChannels
        {
            get
            {
                if (this.SystemType == SystemType.Wii && _wiiChannels == null)
                    populatePartitions();
                return _wiiChannels;
            }
        }

        /// <summary>
        /// Basic lookup: returns the recovery update partition matching the given update CRC, or
        /// null if none is available. Recovery files are named {SHA1}_{TYPE}_{CRC8}; only entries
        /// backed by a real file (Filename != null) are considered. This is the single home for
        /// resolving a recovery update partition by CRC (callers should not scan folders).
        /// </summary>
        public FixPartition FindUpdatePartition(uint crc)
        {
            return this.WiiUpdatePartitions?
                .FirstOrDefault(a => a.Filename != null && a.Crc == crc);
        }

        /// <summary>
        /// Human-readable name for a resolved recovery partition. Recovery files are named by SHA1,
        /// so the parent subfolder (e.g. "Wii System Update (Europe) ...") is the friendlier label;
        /// falls back to the file name.
        /// </summary>
        public static string GetUpdatePartitionDisplayName(FixPartition p)
        {
            if (p == null || string.IsNullOrEmpty(p.Filename))
                return null;
            string name = Path.GetFileName(Path.GetDirectoryName(p.Filename));
            return string.IsNullOrEmpty(name) ? Path.GetFileName(p.Filename) : name;
        }

        public DataPatches[] DataPatches { get; private set; }

        public DirectoryInfo FixPath { get; internal set; }
        public string ForceJunkId { get; private set; }
        public int ChannelCount { get; private set; }
        public Dictionary<byte[], int> RegionData { get; private set; }
        public uint[] FstCrcs { get; private set; }
        public uint[] ApploaderFsts { get; private set; }
        public uint[] RedumpUpdateCrcs { get; private set; }
        //public uint[] RedumpChannelCrcs { get; private set; }

        public SystemType SystemType { get; private set; }

        public void Load(SystemType system, FileInfo info, string filesPath)
        {
            this.SystemType = system;
            _filesPath = filesPath;

            if (info == null)
                return;

            if (info.Exists)
            {
                try
                {
                    string yamlContent = File.ReadAllText(info.FullName);
                    _yaml = AotYamlDeserializer.DeserializeFixFile(yamlContent);
                }
                catch (Exception ex)
                {
                    throw new HandledException(ex, $"Failed to read fixData: {info.FullName}");
                }
            }
        }



        internal void Setup(string id8)
        {
            if (!string.IsNullOrWhiteSpace(_filesPath))
                this.FixPath = new DirectoryInfo(_filesPath);

            if (_yaml != null)
            {

                if (_yaml.ContainsKey("dataPatches"))
                {
                    if (((Dictionary<object, object>)_yaml["dataPatches"]).TryGetValue(id8, out object v))
                        this.DataPatches = ((Dictionary<object, object>)v).Select(a => new DataPatches(id8, long.Parse((string)a.Key, NumberStyles.HexNumber), ((string)a.Value).HexToBytes())).ToArray();
                }

                if (_yaml.ContainsKey("junkIdSubstitutions"))
                {
                    if (((Dictionary<object, object>)_yaml["junkIdSubstitutions"]).TryGetValue(id8, out object v))
                        this.ForceJunkId = (string)v;
                }

                if (_yaml.ContainsKey("redumpFstCrcs"))
                    this.FstCrcs = ((List<object>)_yaml["redumpFstCrcs"]).Select(a => uint.Parse((string)a, NumberStyles.HexNumber)).ToArray();

                if (_yaml.ContainsKey("redumpAppldrCrcs"))
                    this.ApploaderFsts = ((List<object>)_yaml["redumpAppldrCrcs"]).Select(a => uint.Parse((string)a, NumberStyles.HexNumber)).ToArray();

                if (_yaml.ContainsKey("redumpUpdateCrcs"))
                    this.RedumpUpdateCrcs = ((List<object>)_yaml["redumpUpdateCrcs"]).Select(a => uint.Parse((string)a, NumberStyles.HexNumber)).ToArray();

                if (_yaml.ContainsKey("redumpChannels"))
                {
                    if (((Dictionary<object, object>)_yaml["redumpChannels"]).TryGetValue(id8, out object v))
                        this.ChannelCount = int.Parse((string)v);
                }

                if (_yaml.ContainsKey("redumpRegionData"))
                    this.RegionData = ((Dictionary<object, object>)_yaml["redumpRegionData"]).ToDictionary(a => ((string)a.Key).HexToBytes(), a => int.Parse((string)a.Value));
            }
            else
            {
                this.FstCrcs = new uint[0];
                this.ApploaderFsts = new uint[0];
                this.RedumpUpdateCrcs = new uint[0];
                this.RegionData = new Dictionary<byte[], int>();
            }
        }

        private void populatePartitions()
        {
            _wiiChannels = new List<FixPartition>();
            _wiiUpdatePartitions = new List<FixPartition>();
            try
            {
                if (this.FixPath != null && this.FixPath.Exists)
                {
                    Match m;
                    foreach (FileInfo f in this.FixPath.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        // A recovery file may be a plain file named NAME, OR a streamable archive
                        // NAME.zip/.7z/.rar/.gz whose single inner entry is named NAME (Wii
                        // convention). Strip a streamable-archive extension so the SAME name regex
                        // matches either form; when archive-backed, record the inner entry name and
                        // use the inner entry's size for Length (not the archive's compressed size).
                        string ext = f.Extension;
                        bool isArc = SourceFiles.IsStreamableArchiveExtension(ext);
                        string matchName = isArc ? Path.GetFileNameWithoutExtension(f.Name) : f.Name;
                        string innerName = isArc ? matchName : null;

                        if ((m = Regex.Match(matchName, @"^([A-Z0-9_]{10})_([0-9]{2})_([A-Z0-9_]{4})_([A-Z]+)_([A-F0-9]{8})$", RegexOptions.IgnoreCase)).Success)
                        {
                            long len = isArc ? getArchiveEntrySize(f, innerName) : f.Length;
                            this.WiiChannels.Add(new FixPartition(f.FullName, m.Groups[4].Value, len, uint.Parse(m.Groups[5].Value, NumberStyles.HexNumber), m.Groups[1].Value, m.Groups[3].Value) { InnerEntryName = innerName });
                        }
                        else if ((m = Regex.Match(matchName, @"^([A-Z0-9]{40})_([A-Z]+)_([A-F0-9]{8})$", RegexOptions.IgnoreCase)).Success)
                        {
                            long len = isArc ? getArchiveEntrySize(f, innerName) : f.Length;
                            this.WiiUpdatePartitions.Add(new FixPartition(f.FullName, m.Groups[2].Value, len, uint.Parse(m.Groups[3].Value, NumberStyles.HexNumber), m.Groups[1].Value, null) { InnerEntryName = innerName });
                        }
                    }
                }
                if (this.RedumpUpdateCrcs != null)
                {
                    foreach (uint ucrc in this.RedumpUpdateCrcs)
                    {
                        if (!this.WiiUpdatePartitions.Exists(a => a.Crc == ucrc))
                            this.WiiUpdatePartitions.Add(new FixPartition(null, "", 0, ucrc, "", ""));
                    }
                }
            }
            catch (Exception ex)
            {
                throw new HandledException(ex, "Failed to populate update and channel file data");
            }
        }

        // Resolve the uncompressed size of the inner entry (named <paramref name="innerName"/>) of a
        // streamable archive recovery file, using the same scan/read primitives the DatManager uses
        // for dats inside archives. Returns 0 if the entry can't be found (the decorator then serves
        // a zero-length/zero-filled region — no worse than a missing recovery file).
        private static long getArchiveEntrySize(FileInfo archive, string innerName)
        {
            try
            {
                FileMask mask = FileMask.CreateLocalMask($"{archive.FullName}//{innerName}", false);
                foreach (FileItem fi in SourceFileSystem.GetLocalArchiveFiles(mask, false, null, null))
                {
                    if (string.Equals(fi.FileName, innerName, StringComparison.OrdinalIgnoreCase))
                        return fi.Size;
                }
            }
            catch { /* best-effort */ }
            return 0;
        }

        private void populateGcBins()
        {
            _gcBinFiles = new List<FixFileItem>();

            if (this.FixPath?.Exists ?? false)
            {
                foreach (FileInfo fi in this.FixPath.EnumerateFiles())
                {
                    Match m;
                    // A GC fix file may be a plain file named NAME.bin, OR a streamable archive
                    // NAME.bin.zip/.7z/.rar/.gz whose single inner entry is named NAME.bin (same
                    // convention as the Wii recovery files). Strip a streamable-archive extension so
                    // the SAME name regex matches either form; when archive-backed, record the inner
                    // entry name and use the inner entry's uncompressed size for Length.
                    bool isArc = SourceFiles.IsStreamableArchiveExtension(fi.Extension);
                    string matchName = isArc ? Path.GetFileNameWithoutExtension(fi.Name) : fi.Name;
                    string innerName = isArc ? matchName : null;
                    long len = isArc ? getArchiveEntrySize(fi, innerName) : fi.Length;

                    if ((m = Regex.Match(matchName, @"^fst\[(.{10})\]\[(.{8})\]\[(.{8})\]\[(.{8})\]\.bin$")).Success)
                        this.GcBinFiles.Add(new FstFileItem(fi.FullName, "fst", len, uint.Parse(m.Groups[3].Value, NumberStyles.HexNumber), m.Groups[1].Value, uint.Parse(m.Groups[2].Value, NumberStyles.HexNumber), uint.Parse(m.Groups[4].Value, NumberStyles.HexNumber)) { InnerEntryName = innerName });
                    else if ((m = Regex.Match(matchName, @"^appldr\[(.{8})\]\[(.{8})\].bin")).Success)
                        this.GcBinFiles.Add(new ApploaderFileItem(fi.FullName, "appldr", len, uint.Parse(m.Groups[2].Value, NumberStyles.HexNumber)) { InnerEntryName = innerName });
                }
            }
        }

    }

    internal class ApploaderFileItem : FixFileItem
    {
        public ApploaderFileItem(string filename, string type, long length, uint crc) : base(filename, type, length, crc)
        {
            CalcCrc = 0;
        }

        public uint CalcCrc { get; set; }
    }

    internal class FstFileItem : FixFileItem
    {
        public FstFileItem(string filename, string type, long length, uint crc, string id8, uint appLoadCrc, uint postFstCrc) : base(filename, type, length, crc)
        {
            Id8 = id8;
            AppLoadCrc = appLoadCrc;
            PostFstCrc = postFstCrc;
        }

        public void Populate(ILogScope log)
        {
            // Reads a plain .bin or the single entry inside a streamable archive (archive-backed
            // fix file). ReadAllData returns null when unavailable, leaving fields at defaults.
            byte[] file = this.ReadAllData(log);
            if (file != null)
            {
                MainDolOffset = file.ReadUInt32B(0x00);
                FstOffset = file.ReadUInt32B(0x04);
                MaxFst = file.ReadUInt32B(0x08);
                Region = (Region)file.ReadUInt32B(0x0C);
                Title = file.Read(0x10, 0x50 - 0x10);
                FstData = file.Read(0x50, file.Length - 0x50);
            }
        }

        public string Id8 { get; }
        public uint AppLoadCrc { get; }
        public uint PostFstCrc { get; }

        public long FstOffset { get; private set; }
        public long MainDolOffset { get; private set; }
        public Region Region { get; private set; }
        public long MaxFst { get; private set; }
        public byte[] Title { get; private set; }
        public byte[] FstData { get; private set; }
    }
}