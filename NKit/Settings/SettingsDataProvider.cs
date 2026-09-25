using Nanook.NKit.Dats;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nanook.NKit
{
    internal class SettingsDataProvider : IDataProvider
    {
        private SystemType _systemType;
        private ILogScope _log;
        private string _scanIn;
        private string _fixInfo;
        private string _fixFiles;
        private string _keys;
        private DatManager _datManager;
        public int KeyBits => 128;
        public IFixData _fixData;
        public bool _keySupport;
        private byte[][] _keyCache;
        private bool _cacheLoaded;
        private byte[] _initialKey;

        public SettingsDataProvider(SystemType systemType, ILogScope log, string scanIn, string fixInfo, string fixFiles, string keys, DatManager datManager)
        {
            _systemType = systemType;
            _log = log;
            _scanIn = scanIn;
            _fixInfo = fixInfo;
            _fixFiles = fixFiles;
            _keys = keys;
            _datManager = datManager;
            _keySupport = systemType == SystemType.PS3 || systemType == SystemType.WiiU || systemType == SystemType.XBox || systemType == SystemType.XBox360;
            _keyCache = new byte[0][];
            _cacheLoaded = false;
        }

        /// <summary>
        /// Loads all keys from the configured _keys path. Supports two modes:
        /// 
        /// Folder mode (e.g. "keys/wiiu"):
        ///   Scans the folder for key files directly: *.key, *.dkey, *.keys, keys.txt
        /// 
        /// Archive/mask mode (e.g. "keys/wiiu/*.zip" or "keys/wiiu/allkeys.zip"):
        ///   Finds archives matching the mask, opens each one, and scans inside
        ///   for the same key file types: *.key, *.dkey, *.keys, keys.txt
        /// 
        /// Key file formats:
        ///   *.key / *.dkey  - Binary key file, entire content is the key
        ///   *.keys          - Text file, hex key at start of each line
        ///   keys.txt        - Text file, hex key at start of each line
        /// 
        /// Additionally checks the source image's folder (fallbackPath) for a
        /// matching imageName.key or imageName.dkey as an initial/preferred key.
        /// </summary>
        private void loadAllKeysAndInitialKey(string imageName, string fallbackPath)
        {
            if (!_keySupport)
                return;

            if (_cacheLoaded)
                return; // already loaded

            string keyName1 = !string.IsNullOrWhiteSpace(imageName) ? imageName + ".key" : null;
            string keyName2 = !string.IsNullOrWhiteSpace(imageName) ? imageName + ".dkey" : null;
            List<byte[]> keyCache = new List<byte[]>();
            HashSet<string> keySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            //fall back to check the source folder for a key file
            if (!string.IsNullOrWhiteSpace(fallbackPath) && Directory.Exists(fallbackPath))
            {
                FileMask keyMask = FileMask.CreateLocalMask($"{Path.Combine(fallbackPath, imageName + ".key")}|{imageName}.dkey", false);
                foreach (FileItem fi in SourceFileSystem.GetLocalFiles(keyMask, _log, null).Where(a => a.IsMatch)) //checks within archives
                {
                    using (SourceFileSystemReader rdr = SourceFileSystem.CreateReader(fi, _log, null))
                    {
                        byte[] key = keyToBytes(rdr.OpenRead(fi).ReadBytes(fi.Size));
                        if (keySet.Add(key.ToHexString())) //only if unique
                        {
                            keyCache.Add(keyToBytes(key)); //add first matching
                            _initialKey = key;
                            break;
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(_keys))
            {
                if (Directory.Exists(_keys))
                    addKey(imageName, keyName1, keyName2, keyCache, keySet, _keys);
                else
                {
                    FileMask keyMask = FileMask.CreateLocalMask(_keys, false);
                    foreach (FileItem fi in SourceFileSystem.GetLocalFiles(keyMask, _log, null).Where(a => a.IsMatch)) //checks within archives
                        addKey(imageName, keyName1, keyName2, keyCache, keySet, fi.PathFileName);
                }
            }

            _cacheLoaded = true;
            _keyCache = keyCache.ToArray();
        }

        private void addKey(string imageName, string keyName1, string keyName2, List<byte[]> keyCache, HashSet<string> keySet, string path)
        {
            getFile(path,
                nm =>
                    nm.EndsWith(".key", StringComparison.OrdinalIgnoreCase) ||
                    nm.EndsWith(".dkey", StringComparison.OrdinalIgnoreCase) ||
                    nm.EndsWith(".keys", StringComparison.OrdinalIgnoreCase) ||
                    nm.Equals("keys.txt", StringComparison.OrdinalIgnoreCase),
                (nm, strm, sz) =>
                {
                    if (nm.EndsWith(".keys", StringComparison.OrdinalIgnoreCase) || nm.Equals("keys.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        using (StreamReader reader = new StreamReader(strm, Encoding.UTF8, true, 0x400, leaveOpen: true))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                if (line.Length >= 32 && System.Text.RegularExpressions.Regex.IsMatch(line, @"^[0-9a-fA-F]{32}"))
                                {
                                    byte[] key = line.HexToBytes(0, 32);
                                    if (keySet.Add(line.Substring(0, 32).ToUpper())) //only if unique
                                        keyCache.Add(key);
                                }
                            }
                        }
                    }
                    else // .key or .dkey file
                    {
                        byte[] key = keyToBytes(strm.ReadBytes((int)sz));
                        if (key != null)
                        {
                            if (keySet.Add(key.ToHexString())) //only if unique
                            {
                                keyCache.Add(key);
                                if (_initialKey == null && !string.IsNullOrWhiteSpace(imageName) && (nm.Equals(keyName1, StringComparison.OrdinalIgnoreCase) || nm.Equals(keyName2, StringComparison.OrdinalIgnoreCase)))
                                    _initialKey = key;
                            }
                        }
                    }
                    return false; // continue processing all keys
                }
            );
        }

        public string DedupePath { get; internal set; }

        public void LoadFixData(IFixData data)
        {
            _fixData = data;
            _fixData.Load(_systemType, this.GetFixFile(), _fixFiles);
        }

        public T FixData<T>() where T : class, IFixData => _fixData as T;

        public Scan GetNKitScan(string imagePathFileName)
        {
            SourceFiles.GetFileNameParts(imagePathFileName, out string path, out string filename, out string unique, out string ext);

            string scanInPath = _scanIn;

            if (!string.IsNullOrWhiteSpace(scanInPath) && Directory.Exists(scanInPath))
            {
                // A scan may be saved as XML (.nkit) or YAML (.nkit.yaml). Look for either; the
                // parser sniffs the content, so the extension is only used to find the file.
                foreach (string scanExt in new[] { "nkit", NKitTask.ScanExt })
                {
                    FileMask keyMask = FileMask.CreateLocalMask($"{Path.Combine(scanInPath, $"{filename}.{scanExt}")}", false);
                    foreach (FileItem fi in SourceFileSystem.GetLocalFiles(keyMask, _log, null).Where(a => a.IsMatch)) //checks within archives
                    {
                        using (SourceFileSystemReader rdr = SourceFileSystem.CreateReader(fi, _log, null))
                            return ScanParserYaml.Parse(rdr.OpenRead(fi), fi.PathFileName); //format-agnostic (XML or YAML); return first matching
                    }
                }
            }

            return null;
        }

        public FileInfo GetFixFile()
        {
            if (string.IsNullOrWhiteSpace(_fixInfo))
                return null;
            string fixFn = _fixInfo;
            if (File.Exists(fixFn))
                return new FileInfo(Path.IsPathRooted(fixFn) ? fixFn : Path.Combine(AppSettings.ThisExe.DirectoryName, fixFn));
            return null;
        }

        public byte[] GetKey(uint crc)
        {
            if (!_cacheLoaded)
                loadAllKeysAndInitialKey(null, null);

            if (_keyCache != null)
            {
                foreach (byte[] candidate in _keyCache)
                {
                    if (Crc.Compute(candidate) == crc)
                        return candidate;
                }
            }

            byte[] data = null;
            getFile(_keys,
                nm => true,
                (nm, strm, sz) =>
                {
                    byte[] key = strm.ReadBytes(sz);
                    strm.Close();
                    if (Crc.Compute(keyToBytes(key)) == crc)
                        data = keyToBytes(key);
                    return data != null; //exit search
                });
            return data;
        }

        public byte[] GetKey(string imageName, string fallbackPath)
        {
            loadAllKeysAndInitialKey(imageName, fallbackPath);

            return _initialKey;
        }

        public byte[][] AllKeys => _keyCache;

        private byte[] keyToBytes(byte[] key)
        {
            byte[] data = null;
            if (key != null)
            {
                if (key.Length == this.KeyBits / 8)
                    data = key;
                else if (key.Length >= this.KeyBits / 4) //ascii
                    data = key.HexToBytes(0, this.KeyBits / 4);
            }
            return data ?? key;
        }

        private void getFile(string path, Func<string, bool> test, Func<string, Stream, long, bool> read)
        {
            if (string.IsNullOrEmpty(path))
                return;


            if (Directory.Exists(path))
            {
                foreach (string fn in Directory.GetFiles(path))
                {
                    if (test(Path.GetFileName(fn)) && read(Path.GetFileName(fn), File.OpenRead(fn), new FileInfo(fn).Length))
                        return;
                }
            }
            else
            {
                try
                {
                    if (File.Exists(path))
                    {
                        using (Stream stream = File.OpenRead(path))
                        {
                            using (IArchive archive = ArchiveFactory.OpenArchive(stream)) //handles multipart archives
                            {
                                foreach (IArchiveEntry entry in archive.Entries)
                                {
                                    if (test(Path.GetFileName(entry.Key)) && read(Path.GetFileName(entry.Key), entry.OpenEntryStream(), entry.Size))
                                        return;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new HandledException(ex, $"Failed to open directory or archive");
                }
            }
        }

        public DatItem GetDat(Predicate<DatItem> match) => _datManager.FindMatch(match);
    }
}